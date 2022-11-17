﻿using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BossMod
{
    // to execute a concrete plan for a concrete encounter, we build a "timeline" - for each state we assign a time/duration (which depend both on state machine definition and plan's phase timings)
    // for each action defined by a plan, we define start/end times on that timeline (plus a range of branches in which it might be executed)
    public class CooldownPlanExecution
    {
        public struct StateFlag
        {
            public bool Active;
            public float TransitionIn;
        }

        public class StateData
        {
            public float EnterTime;
            public float Duration;
            public int BranchID;
            public int NumBranches;
            public StateFlag Downtime = new() { TransitionIn = float.MaxValue };
            public StateFlag Positioning = new() { TransitionIn = float.MaxValue };
            public StateFlag Vulnerable = new() { TransitionIn = float.MaxValue };
        }

        public class ActionData
        {
            public ActionID ID;
            public float WindowStart;
            public float WindowEnd;
            public int BranchID;
            public int NumBranches;
            public bool LowPriority;
            public bool Executed;
            public PlanTarget.ISelector Target;
            // TODO: condition, etc.

            public bool IntersectBranchRange(int branchID, int numBranches) => BranchID < branchID + numBranches && branchID < BranchID + NumBranches;

            public ActionData(ActionID id, float windowStart, float windowEnd, int branchID, int numBranches, bool lowPriority, PlanTarget.ISelector target)
            {
                ID = id;
                WindowStart = windowStart;
                WindowEnd = windowEnd;
                BranchID = branchID;
                NumBranches = numBranches;
                LowPriority = lowPriority;
                Target = target;
            }
        }

        public CooldownPlan? Plan { get; private init; }
        private StateData Pull = new();
        private Dictionary<uint, StateData> States = new();
        private List<ActionData> Actions = new();

        public CooldownPlanExecution(StateMachine sm, CooldownPlan? plan)
        {
            Plan = plan;

            var tree = new StateMachineTree(sm);
            tree.ApplyTimings(plan?.Timings);

            StateData? nextPhaseStart = null;
            for (int i = tree.Phases.Count - 1; i >= 0; i--)
                nextPhaseStart = ProcessState(tree, tree.Phases[i].StartingNode, null, nextPhaseStart);
            UpdateTransitions(Pull, nextPhaseStart);

            if (plan != null)
            {
                foreach (var a in plan.Actions)
                {
                    var s = States.GetValueOrDefault(a.StateID);
                    if (s != null)
                    {
                        var windowStart = s.EnterTime + Math.Min(s.Duration, a.TimeSinceActivation);
                        Actions.Add(new(a.ID, windowStart, windowStart + a.WindowLength, s.BranchID, s.NumBranches, a.LowPriority, a.Target));
                    }
                }
            }
        }

        public StateData FindStateData(StateMachine.State? s)
        {
            var state = s != null ? States.GetValueOrDefault(s.ID) : null;
            return state ?? Pull;
        }

        // all such functions return whether flag is currently active + estimated time to transition
        public (bool, float) EstimateTimeToNextDowntime(StateMachine sm)
        {
            var s = FindStateData(sm.ActiveState);
            return (s.Downtime.Active, s.Downtime.TransitionIn - Math.Min(sm.TimeSinceTransition, s.Duration));
        }

        public (bool, float) EstimateTimeToNextPositioning(StateMachine sm)
        {
            var s = FindStateData(sm.ActiveState);
            return (s.Positioning.Active, s.Positioning.TransitionIn - Math.Min(sm.TimeSinceTransition, s.Duration));
        }

        public (bool, float) EstimateTimeToNextVulnerable(StateMachine sm)
        {
            var s = FindStateData(sm.ActiveState);
            return (s.Vulnerable.Active, s.Vulnerable.TransitionIn - Math.Min(sm.TimeSinceTransition, s.Duration));
        }

        public IEnumerable<(ActionID Action, float TimeLeft, PlanTarget.ISelector Target, bool LowPriority)> ActiveActions(StateMachine sm)
        {
            var s = FindStateData(sm.ActiveState);
            var t = s.EnterTime + Math.Min(sm.TimeSinceTransition, s.Duration);
            return Actions.Where(a => !a.Executed && t >= a.WindowStart && t <= a.WindowEnd && a.IntersectBranchRange(s.BranchID, s.NumBranches)).Select(a => (a.ID, a.WindowEnd - t, a.Target, a.LowPriority));
        }

        public void NotifyActionExecuted(StateMachine sm, ActionID action)
        {
            // TODO: not sure what to do if we have several overlapping requests for same action, do we really mark all of them as executed?..
            var s = FindStateData(sm.ActiveState);
            var t = s.EnterTime + Math.Min(sm.TimeSinceTransition, s.Duration);
            foreach (var a in Actions.Where(a => a.ID == action && t >= a.WindowStart && t <= a.WindowEnd && a.IntersectBranchRange(s.BranchID, s.NumBranches)))
                a.Executed = true;
        }

        public void Draw(StateMachine sm)
        {
            if (Plan == null)
                return;
            var s = FindStateData(sm.ActiveState);
            var t = s.EnterTime + Math.Min(sm.TimeSinceTransition, s.Duration);
            var classDef = PlanDefinitions.Classes[Plan.Class];
            foreach (var track in classDef.CooldownTracks)
            {
                var next = FindNextActionInTrack(track.Actions.Select(a => a.aid), t, s.BranchID, s.NumBranches);
                if (next == null)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, 0x80808080);
                    ImGui.TextUnformatted(track.Name);
                    ImGui.PopStyleColor();
                }
                else if (next.WindowStart <= t)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, 0xff00ffff);
                    ImGui.TextUnformatted($"{track.Name}: use now! ({next.WindowEnd - t:f1}s left)");
                    ImGui.PopStyleColor();
                }
                else
                {
                    var left = next.WindowStart - t;
                    ImGui.PushStyleColor(ImGuiCol.Text, left < classDef.Abilities[track.Actions[0].aid].Cooldown ? 0xffffffff : 0x80808080);
                    ImGui.TextUnformatted($"{track.Name}: in {left:f1}s");
                    ImGui.PopStyleColor();
                }
            }
        }

        private StateData ProcessState(StateMachineTree tree, StateMachineTree.Node curState, StateData? prev, StateData? nextPhaseStart)
        {
            var curPhase = tree.Phases[curState.PhaseID];

            // phaseLeft < 0 if cur state exit is after expected phase exit
            var phaseLeft = curPhase.Duration - curState.Time;

            var s = States[curState.State.ID] = new();
            s.EnterTime = prev != null ? prev.EnterTime + prev.Duration : curPhase.StartTime;
            s.Duration = Math.Clamp(curState.State.Duration + phaseLeft, 0, curState.State.Duration);
            s.BranchID = curState.BranchID;
            s.NumBranches = curState.NumBranches;
            s.Downtime.Active = curState.IsDowntime;
            s.Positioning.Active = curState.IsPositioning;
            s.Vulnerable.Active = curState.IsVulnerable;

            // process successor states of the same phase
            // note that we might not expect to reach them due to phase timings, but they still might be reached in practice (if we're going slower than intended)
            // in such case all successors will have identical enter-time (equal to enter-time of initial state of next phase) and identical duration (zero)
            if (curState.Successors.Count == 0)
            {
                UpdateTransitions(s, nextPhaseStart);
            }
            else
            {
                foreach (var succ in curState.Successors)
                {
                    var succState = ProcessState(tree, succ, s, nextPhaseStart);
                    UpdateTransitions(s, succState);
                }
            }

            return s;
        }

        private void UpdateTransitions(StateData s, StateData? next)
        {
            UpdateFlagTransition(ref s.Downtime, next?.Downtime ?? new() { Active = true, TransitionIn = 10000 }, s.Duration);
            UpdateFlagTransition(ref s.Positioning, next?.Positioning ?? new() { Active = false, TransitionIn = 10000 }, s.Duration);
            UpdateFlagTransition(ref s.Vulnerable, next?.Vulnerable ?? new() { Active = false, TransitionIn = 10000 }, s.Duration);
        }

        private void UpdateFlagTransition(ref StateFlag curFlag, StateFlag nextFlag, float curDuration)
        {
            var transition = (curFlag.Active == nextFlag.Active ? nextFlag.TransitionIn : 0) + curDuration;
            curFlag.TransitionIn = Math.Min(curFlag.TransitionIn, transition); // in case state has multiple successors, take minimal time to transition (TODO: is that right?..)
        }

        // note: current implementation won't work well with overlapping windows
        private ActionData? FindNextActionInTrack(IEnumerable<ActionID> filter, float time, int branchID, int numBranches)
        {
            ActionData? res = null;
            foreach (var a in Actions.Where(a => !a.Executed && a.IntersectBranchRange(branchID, numBranches) && a.WindowEnd > time && filter.Contains(a.ID)))
                if (res == null || a.WindowEnd < res.WindowEnd)
                    res = a;
            return res;
        }

        private ActionData? FindNthActionInTrack(IEnumerable<ActionID> filter, float time, int branchID, int numBranches, int skip)
        {
            var next = FindNextActionInTrack(filter, time, branchID, numBranches);
            while (next != null && skip > 0)
            {
                next = FindNextActionInTrack(filter, next.WindowEnd, next.BranchID, next.NumBranches);
                --skip;
            }
            return next;
        }
    }
}
