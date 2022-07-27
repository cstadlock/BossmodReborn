﻿using Dalamud.Game.Network;
using Dalamud.Hooking;
using System;
using System.IO;
using System.Numerics;

namespace BossMod
{
    class Network : IDisposable
    {
        public struct PendingAction
        {
            public ActionID Action;
            public ulong TargetID;
            public uint Sequence;
        }

        public event EventHandler<(ulong actorID, ActorCastEvent cast)>? EventActionEffect;
        public event EventHandler<(ulong actorID, ActionID action, float castTime, ulong targetID)>? EventActorCast;
        public event EventHandler<(ulong actorID, uint actionID)>? EventActorControlCancelCast;
        public event EventHandler<(ulong actorID, uint iconID)>? EventActorControlTargetIcon;
        public event EventHandler<(ulong actorID, ulong targetID, uint tetherID)>? EventActorControlTether;
        public event EventHandler<ulong>? EventActorControlTetherCancel;
        public event EventHandler<(ulong actorID, uint actionID, uint sourceSequence)>? EventActorControlSelfActionRejected;
        public event EventHandler<(uint directorID, uint updateID, uint p1, uint p2, uint p3, uint p4)>? EventActorControlSelfDirectorUpdate;
        public event EventHandler<(uint directorID, byte index, uint state)>? EventEnvControl;
        public event EventHandler<(Waymark waymark, Vector3? pos)>? EventWaymark;
        public event EventHandler<PendingAction>? EventActionRequest;
        public event EventHandler<PendingAction>? EventActionRequestGT;

        private GeneralConfig _config;
        //private Logger _logger;

        private unsafe delegate void ProcessZonePacketDownDelegate(void* a, uint targetId, void* dataPtr);
        private Hook<ProcessZonePacketDownDelegate> _processZonePacketDownHook;

        private unsafe delegate byte ProcessZonePacketUpDelegate(void* a1, void* dataPtr, void* a3, byte a4);
        private Hook<ProcessZonePacketUpDelegate> _processZonePacketUpHook;

        // this is a mega weird thing - apparently some IDs sent over network have some extra delta added to them (e.g. action ids, icon ids, etc.)
        // they change on relogs or zone changes or something...
        // we have one simple way of detecting them - by looking at casts, since they contain both offset id and real ('animation') id
        private int _unkDelta = 0;

        public unsafe Network(DirectoryInfo logDir)
        {
            _config = Service.Config.Get<GeneralConfig>();
            _config.Modified += ApplyConfig;
            //_logger = new("Network", logDir);

            // this is lifted from dalamud - for some reason they stopped dispatching client messages :(
            //Service.GameNetwork.NetworkMessage += HandleMessage;
            var processZonePacketDownAddress = Service.SigScanner.ScanText("48 89 5C 24 ?? 56 48 83 EC 50 8B F2");
            _processZonePacketDownHook = Hook<ProcessZonePacketDownDelegate>.FromAddress(processZonePacketDownAddress, ProcessZonePacketDownDetour);
            _processZonePacketDownHook.Enable();

            var processZonePacketUpAddress = Service.SigScanner.ScanText("48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 41 56 41 57 48 83 EC 70 8B 81 ?? ?? ?? ??");
            _processZonePacketUpHook = Hook<ProcessZonePacketUpDelegate>.FromAddress(processZonePacketUpAddress, ProcessZonePacketUpDetour);
            _processZonePacketUpHook.Enable();
        }

        public void Dispose()
        {
            _config.Modified -= ApplyConfig;
            //_logger.Deactivate();

            //Service.GameNetwork.NetworkMessage -= HandleMessage;
            _processZonePacketDownHook.Dispose();
            _processZonePacketUpHook.Dispose();
        }

        private void ApplyConfig(object? sender, EventArgs args)
        {
            //if (_config.DumpServerPackets)
            //{
            //    if (!_logger.Active)
            //        _logger.Activate(0);
            //}
            //else
            //{
            //    _logger.Deactivate();
            //}
        }

        private unsafe void ProcessZonePacketDownDetour(void* self, uint targetId, void* dataPtr)
        {
            HandleMessage((IntPtr)dataPtr + sizeof(Protocol.Server_IPCHeader), ((Protocol.Server_IPCHeader*)dataPtr)->MessageType, 0, targetId, NetworkMessageDirection.ZoneDown);
            _processZonePacketDownHook.Original(self, targetId, dataPtr);
        }

        private unsafe byte ProcessZonePacketUpDetour(void* self, void* dataPtr, void* a3, byte a4)
        {
            HandleMessage((IntPtr)dataPtr + 0x20, Utils.ReadField<ushort>(dataPtr, 0), 0, 0, NetworkMessageDirection.ZoneUp);
            return _processZonePacketUpHook.Original(self, dataPtr, a3, a4);
        }

        private unsafe void HandleMessage(IntPtr dataPtr, ushort opCode, uint sourceActorId, uint targetActorId, NetworkMessageDirection direction)
        {
            if (direction == NetworkMessageDirection.ZoneDown)
            {
                // server->client
                if (_config.DumpServerPackets)
                {
                    DumpServerMessage(dataPtr, opCode, targetActorId);
                }

                switch ((Protocol.Opcode)opCode)
                {
                    case Protocol.Opcode.ActionEffect1:
                        HandleActionEffect1((Protocol.Server_ActionEffect1*)dataPtr, targetActorId);
                        break;
                    case Protocol.Opcode.ActionEffect8:
                        HandleActionEffect8((Protocol.Server_ActionEffect8*)dataPtr, targetActorId);
                        break;
                    case Protocol.Opcode.ActionEffect16:
                        HandleActionEffect16((Protocol.Server_ActionEffect16*)dataPtr, targetActorId);
                        break;
                    case Protocol.Opcode.ActionEffect24:
                        HandleActionEffect24((Protocol.Server_ActionEffect24*)dataPtr, targetActorId);
                        break;
                    case Protocol.Opcode.ActionEffect32:
                        HandleActionEffect32((Protocol.Server_ActionEffect32*)dataPtr, targetActorId);
                        break;
                    case Protocol.Opcode.ActorCast:
                        HandleActorCast((Protocol.Server_ActorCast*)dataPtr, targetActorId);
                        break;
                    case Protocol.Opcode.ActorControl:
                        HandleActorControl((Protocol.Server_ActorControl*)dataPtr, targetActorId);
                        break;
                    case Protocol.Opcode.ActorControlSelf:
                        HandleActorControlSelf((Protocol.Server_ActorControlSelf*)dataPtr, targetActorId);
                        break;
                    case Protocol.Opcode.EnvironmentControl:
                        HandleEnvironmentControl((Protocol.Server_EnvironmentControl*)dataPtr, targetActorId);
                        break;
                    case Protocol.Opcode.Waymark:
                        HandleWaymark((Protocol.Server_Waymark*)dataPtr);
                        break;
                    case Protocol.Opcode.PresetWaymark:
                        HandlePresetWaymark((Protocol.Server_PresetWaymark*)dataPtr);
                        break;
                }
            }
            else
            {
                // client->server
                if (_config.DumpClientPackets)
                {
                    DumpClientMessage(dataPtr, opCode);
                }

                switch ((Protocol.Opcode)opCode)
                {
                    case Protocol.Opcode.ActionRequest:
                        HandleActionRequest((Protocol.Client_ActionRequest*)dataPtr);
                        break;
                    case Protocol.Opcode.ActionRequestGroundTargeted:
                        HandleActionRequestGT((Protocol.Client_ActionRequestGroundTargeted*)dataPtr);
                        break;
                }
            }
        }

        private unsafe void HandleActionEffect1(Protocol.Server_ActionEffect1* p, uint actorID)
        {
            HandleActionEffect(actorID, &p->Header, (ActionEffect*)p->Effects, p->TargetID, 1);
        }

        private unsafe void HandleActionEffect8(Protocol.Server_ActionEffect8* p, uint actorID)
        {
            HandleActionEffect(actorID, &p->Header, (ActionEffect*)p->Effects, p->TargetID, 8);
        }

        private unsafe void HandleActionEffect16(Protocol.Server_ActionEffect16* p, uint actorID)
        {
            HandleActionEffect(actorID, &p->Header, (ActionEffect*)p->Effects, p->TargetID, 16);
        }

        private unsafe void HandleActionEffect24(Protocol.Server_ActionEffect24* p, uint actorID)
        {
            HandleActionEffect(actorID, &p->Header, (ActionEffect*)p->Effects, p->TargetID, 24);
        }

        private unsafe void HandleActionEffect32(Protocol.Server_ActionEffect32* p, uint actorID)
        {
            HandleActionEffect(actorID, &p->Header, (ActionEffect*)p->Effects, p->TargetID, 32);
        }

        private unsafe void HandleActionEffect(uint casterID, Protocol.Server_ActionEffectHeader* header, ActionEffect* effects, ulong* targetIDs, uint maxTargets)
        {
            if (header->actionType == ActionType.Spell)
            {
                int newDelta = (int)header->actionId - (int)header->actionAnimationId;
                if (_unkDelta != newDelta)
                {
                    Service.Log($"Updating network delta: {_unkDelta} -> {newDelta}");
                    _unkDelta = newDelta;
                }
            }

            var info = new ActorCastEvent
            {
                Action = new(header->actionType, (uint)(header->actionId - _unkDelta)), // note: see _unkDelta comment
                MainTargetID = header->animationTargetId,
                AnimationLockTime = header->animationLockTime,
                MaxTargets = maxTargets,
                SourceSequence = header->SourceSequence
            };

            var targets = Math.Min(header->NumTargets, maxTargets);
            for (int i = 0; i < targets; ++i)
            {
                ulong targetID = targetIDs[i];
                if (targetID != 0)
                {
                    var target = new ActorCastEvent.Target();
                    target.ID = targetID;
                    for (int j = 0; j < 8; ++j)
                        target.Effects[j] = *(ulong*)(effects + (i * 8) + j);
                    info.Targets.Add(target);
                }
            }

            EventActionEffect?.Invoke(this, (casterID, info));
        }

        private unsafe void HandleActorCast(Protocol.Server_ActorCast* p, uint actorID)
        {
            EventActorCast?.Invoke(this, (actorID, new(p->SkillType, p->ActionID), p->CastTime, p->TargetID));
        }

        private unsafe void HandleActorControl(Protocol.Server_ActorControl* p, uint actorID)
        {
            switch (p->category)
            {
                case Protocol.Server_ActorControlCategory.CancelCast:
                    EventActorControlCancelCast?.Invoke(this, (actorID, p->param3));
                    break;
                case Protocol.Server_ActorControlCategory.TargetIcon:
                    EventActorControlTargetIcon?.Invoke(this, (actorID, (uint)(p->param1 - _unkDelta)));
                    break;
                case Protocol.Server_ActorControlCategory.Tether:
                    EventActorControlTether?.Invoke(this, (actorID, p->param3, p->param2));
                    break;
                case Protocol.Server_ActorControlCategory.TetherCancel:
                    EventActorControlTetherCancel?.Invoke(this, actorID);
                    break;
            }
        }

        private unsafe void HandleActorControlSelf(Protocol.Server_ActorControlSelf* p, uint actorID)
        {
            switch (p->category)
            {
                case Protocol.Server_ActorControlCategory.ActionRejected:
                    EventActorControlSelfActionRejected?.Invoke(this, (actorID, p->param3, p->param6));
                    break;
                case Protocol.Server_ActorControlCategory.DirectorUpdate:
                    EventActorControlSelfDirectorUpdate?.Invoke(this, (p->param1, p->param2, p->param3, p->param4, p->param5, p->param6));
                    break;
            }
        }

        private unsafe void HandleEnvironmentControl(Protocol.Server_EnvironmentControl* p, uint actorID)
        {
            EventEnvControl?.Invoke(this, (p->FeatureID, p->Index, p->State));
        }

        private unsafe void HandleWaymark(Protocol.Server_Waymark* p)
        {
            if (p->Waymark < Waymark.Count)
                EventWaymark?.Invoke(this, (p->Waymark, p->Active != 0 ? new Vector3(p->PosX / 1000.0f, p->PosY / 1000.0f, p->PosZ / 1000.0f) : null));
        }

        private unsafe void HandlePresetWaymark(Protocol.Server_PresetWaymark* p)
        {
            byte mask = 1;
            for (var i = Waymark.A; i < Waymark.Count; ++i)
            {
                EventWaymark?.Invoke(this, (i, (p->WaymarkMask & mask) != 0 ? new Vector3(p->PosX[(byte)i] / 1000.0f, p->PosY[(byte)i] / 1000.0f, p->PosZ[(byte)i] / 1000.0f) : null));
                mask <<= 1;
            }
        }

        private unsafe void HandleActionRequest(Protocol.Client_ActionRequest* p)
        {
            EventActionRequest?.Invoke(this, new() { Action = new(p->Type, p->ActionID), TargetID = p->TargetID, Sequence = p->Sequence });
        }

        private unsafe void HandleActionRequestGT(Protocol.Client_ActionRequestGroundTargeted* p)
        {
            EventActionRequestGT?.Invoke(this, new() { Action = new(p->Type, p->ActionID), TargetID = 0, Sequence = p->Sequence });
        }

        private unsafe void DumpClientMessage(IntPtr dataPtr, ushort opCode)
        {
            Service.Log($"[Network] Client message {(Protocol.Opcode)opCode}");
            switch ((Protocol.Opcode)opCode)
            {
                case Protocol.Opcode.ActionRequest:
                    {
                        var p = (Protocol.Client_ActionRequest*)dataPtr;
                        Service.Log($"[Network] - AID={new ActionID(p->Type, p->ActionID)}, target={Utils.ObjectString(p->TargetID)}, seq={p->Sequence}, itemsrc={p->ItemSourceContainer}:{p->ItemSourceSlot}, u={p->u0:X2} {p->u1:X4} {p->u2:X4} {p->u3:X8} {p->u5:X8}");
                        break;
                    }
                case Protocol.Opcode.ActionRequestGroundTargeted:
                    {
                        var p = (Protocol.Client_ActionRequestGroundTargeted*)dataPtr;
                        Service.Log($"[Network] - AID={new ActionID(p->Type, p->ActionID)}, seq={p->Sequence}, u={p->u0:X2} {p->u1:X4} {p->u2:X4} {p->u3:X8} {p->u4:X8} {p->u5:X8} {p->u6:X8} {p->u7:X8}");
                        break;
                    }
            }
        }

        private unsafe void DumpServerMessage(IntPtr dataPtr, ushort opCode, uint targetActorId)
        {
            var header = (Protocol.Server_IPCHeader*)(dataPtr - 0x10);
            Service.Log($"[Network] Server message {(Protocol.Opcode)opCode} -> {Utils.ObjectString(targetActorId)} (seq={header->Epoch}): {*(ulong*)dataPtr:X16}...");
            switch ((Protocol.Opcode)opCode)
            {
                case Protocol.Opcode.ActionEffect1:
                    {
                        var p = (Protocol.Server_ActionEffect1*)dataPtr;
                        DumpActionEffect(&p->Header, (ActionEffect*)p->Effects, p->TargetID, 1, 0, 0);
                        break;
                    }
                case Protocol.Opcode.ActionEffect8:
                    {
                        var p = (Protocol.Server_ActionEffect8*)dataPtr;
                        DumpActionEffect(&p->Header, (ActionEffect*)p->Effects, p->TargetID, 8, p->effectflags1, p->effectflags2);
                        break;
                    }
                case Protocol.Opcode.ActionEffect16:
                    {
                        var p = (Protocol.Server_ActionEffect16*)dataPtr;
                        DumpActionEffect(&p->Header, (ActionEffect*)p->Effects, p->TargetID, 16, p->effectflags1, p->effectflags2);
                        break;
                    }
                case Protocol.Opcode.ActionEffect24:
                    {
                        var p = (Protocol.Server_ActionEffect24*)dataPtr;
                        DumpActionEffect(&p->Header, (ActionEffect*)p->Effects, p->TargetID, 24, p->effectflags1, p->effectflags2);
                        break;
                    }
                case Protocol.Opcode.ActionEffect32:
                    {
                        var p = (Protocol.Server_ActionEffect32*)dataPtr;
                        DumpActionEffect(&p->Header, (ActionEffect*)p->Effects, p->TargetID, 32, p->effectflags1, p->effectflags2);
                        break;
                    }
                case Protocol.Opcode.ActorCast:
                    {
                        var p = (Protocol.Server_ActorCast*)dataPtr;
                        Service.Log($"[Network] - AID={new ActionID(p->SkillType, p->ActionID)}, target={Utils.ObjectString(p->TargetID)}, time={p->CastTime:f2}, rot={p->Rotation:f3}, x={p->PosX}, y={p->PosY}, z={p->PosZ}, u={p->Unknown:X2}, u1={new ActionID(ActionType.Spell, p->Unknown1)}, u2={Utils.ObjectString(p->Unknown2)}, u3={p->Unknown3:X4}");
                        break;
                    }
                case Protocol.Opcode.ActorControl:
                    {
                        var p = (Protocol.Server_ActorControl*)dataPtr;
                        Service.Log($"[Network] - cat={p->category}, params={p->param1:X8} {p->param2:X8} {p->param3:X8} {p->param4:X8} {p->param5:X8}, unk={p->unk0:X4}");
                        switch (p->category)
                        {
                            case Protocol.Server_ActorControlCategory.CancelCast: // note: some successful boss casts have this message on completion, seen param1=param4=0, param2=1; param1 is related to cast time?..
                                Service.Log($"[Network] -- cancelled {new ActionID((ActionType)p->param2, p->param3)}, interrupted={p->param4 == 1}");
                                break;
                            case Protocol.Server_ActorControlCategory.GainEffect: // gain status effect, seen param2=param3=param4=0
                                Service.Log($"[Network] -- gained {Utils.StatusString(p->param1)}");
                                break;
                            case Protocol.Server_ActorControlCategory.LoseEffect: // lose status effect, seen param2=param4=0, param3=invalid-oid
                                Service.Log($"[Network] -- lost {Utils.StatusString(p->param1)}");
                                break;
                        }
                        break;
                    }
                case Protocol.Opcode.ActorControlSelf:
                    {
                        var p = (Protocol.Server_ActorControlSelf*)dataPtr;
                        Service.Log($"[Network] - cat={p->category}, params={p->param1:X8} {p->param2:X8} {p->param3:X8} {p->param4:X8} {p->param5:X8} {p->param6:X8} {p->param7:X8}, unk={p->unk0:X4}");
                        switch (p->category)
                        {
                            case Protocol.Server_ActorControlCategory.Cooldown:
                                Service.Log($"[Network] -- group={p->param1}, action={new ActionID(ActionType.Spell, p->param2)}, time={p->param3 / 100.0f:f2}s");
                                break;
                        }
                        break;
                    }
                case Protocol.Opcode.ActorControlTarget:
                    {
                        var p = (Protocol.Server_ActorControlTarget*)dataPtr;
                        Service.Log($"[Network] - cat={p->category}, target={Utils.ObjectString(p->TargetID)}, params={p->param1:X8} {p->param2:X8} {p->param3:X8} {p->param4:X8} {p->param5:X8}, unk={p->unk0:X4} {p->unk1:X8}");
                        break;
                    }
                case Protocol.Opcode.ActorGauge:
                    {
                        var p = (Protocol.Server_ActorGauge*)dataPtr;
                        Service.Log($"[Network] - {p->ClassJobID} = {p->Payload:X16}, u={p->u5:X2} {p->u6:X4} {p->u8:X16}");
                        break;
                    }
                case Protocol.Opcode.EffectResult:
                    {
                        var p = (Protocol.Server_EffectResult*)dataPtr;
                        Service.Log($"[Network] - seq={p->RelatedActionSequence}, actor={Utils.ObjectString(p->ActorID)}, hp={p->CurrentHP}/{p->MaxHP}, mp={p->CurrentMP}, shield={p->DamageShield}, u={p->Unknown1:X8} {p->Unknown3:X4} {p->Unknown6:X4}");
                        var cnt = Math.Min(4, (int)p->EffectCount);
                        for (int i = 0; i < cnt; ++i)
                        {
                            var eff = ((Protocol.Server_EffectResultEntry*)p->Effects) + i;
                            Service.Log($"[Network] -- idx={eff->EffectIndex}, id={Utils.StatusString(eff->EffectID)}, dur={eff->duration:f2}, src={Utils.ObjectString(eff->SourceActorID)}, u={eff->unknown1:X2} {eff->unknown2:X4} {eff->unknown3:X4}");
                        }
                        break;
                    }
                case Protocol.Opcode.EffectResultBasic:
                    {
                        var p = (Protocol.Server_EffectResultBasic*)dataPtr;
                        Service.Log($"[Network] - seq={p->RelatedActionSequence}, actor={Utils.ObjectString(p->ActorID)}, hp={p->CurrentHP}, u={p->Unknown1:X8} {p->Unknown2:X8} {p->Unknown3:X4} {p->Unknown4:X4}");
                        break;
                    }
                case Protocol.Opcode.Waymark:
                    {
                        var p = (Protocol.Server_Waymark*)dataPtr;
                        Service.Log($"[Network] - {p->Waymark}: {p->Active} at {p->PosX / 1000.0f:f3} {p->PosY / 1000.0f:f3} {p->PosZ / 1000.0f:f3}");
                        break;
                    }
                case Protocol.Opcode.PresetWaymark:
                    {
                        var p = (Protocol.Server_PresetWaymark*)dataPtr;
                        for (int i = 0; i < 8; ++i)
                        {
                            Service.Log($"[Network] - {(Waymark)i}: {(p->WaymarkMask & (1 << i)) != 0} at {p->PosX[i] / 1000.0f:f3} {p->PosY[i] / 1000.0f:f3} {p->PosZ[i] / 1000.0f:f3}");
                        }
                        break;
                    }
                case Protocol.Opcode.EnvironmentControl:
                    {
                        var p = (Protocol.Server_EnvironmentControl*)dataPtr;
                        Service.Log($"[Network] - {p->FeatureID:X8}.{p->Index:X2}: {p->State:X8}, u={p->u0:X2} {p->u1:X4} {p->u2:X8}");
                        break;
                    }
                case Protocol.Opcode.UpdateRecastTimes:
                    {
                        var p = (Protocol.Server_UpdateRecastTimes*)dataPtr;
                        Service.Log($"[Network] - {p->Elapsed[0]:f1}/{p->Total[0]:f1}, ..., {p->Elapsed[21]:f1}/{p->Total[21]:f1}");
                        break;
                    }
            }
        }

        private unsafe void DumpActionEffect(Protocol.Server_ActionEffectHeader* data, ActionEffect* effects, ulong* targetIDs, uint maxTargets, uint flags1, ushort flags2)
        {
            // rotation: 0 -> -180, 65535 -> +180
            float rot = (data->rotation / 65535.0f * 360.0f) - 180.0f;
            uint aid = (uint)(data->actionId - _unkDelta);
            Service.Log($"[Network] - AID={new ActionID(data->actionType, aid)} (real={data->actionId}, anim={data->actionAnimationId}), animTarget={Utils.ObjectString(data->animationTargetId)}, animLock={data->animationLockTime:f2}, seq={data->SourceSequence}, cntr={data->globalEffectCounter}, rot={rot:f0}, var={data->variation}, flags={flags1:X8} {flags2:X4}, someTarget={Utils.ObjectString(data->SomeTargetID)}, u={data->unknown20:X2} {data->padding21:X4}");
            var targets = Math.Min(data->NumTargets, maxTargets);
            for (int i = 0; i < targets; ++i)
            {
                ulong targetId = targetIDs[i];
                if (targetId == 0)
                    continue;

                Service.Log($"[Network] -- target {i} == {Utils.ObjectString(targetId)}");
                for (int j = 0; j < 8; ++j)
                {
                    ActionEffect* eff = effects + (i * 8) + j;
                    if (eff->Type == ActionEffectType.Nothing)
                        continue;

                    Service.Log($"[Network] --- effect {j} == {eff->Type}, params={eff->Param0:X2} {eff->Param1:X2} {eff->Param2:X2} {eff->Param3:X2} {eff->Param4:X2} {eff->Value:X4}");
                }
            }
        }
    }
}
