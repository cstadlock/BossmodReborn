﻿using System.Numerics;

namespace BossMod
{
    public abstract class AOEShape
    {
        public abstract bool Check(Vector3 position, Vector3 origin, float rotation);
        public abstract void Draw(MiniArena arena, Vector3 origin, float rotation);

        public bool Check(Vector3 position, Actor? origin)
        {
            return origin != null ? Check(position, origin.Position, origin.Rotation) : false;
        }

        public void Draw(MiniArena arena, Actor? origin)
        {
            if (origin != null)
                Draw(arena, origin.Position, origin.Rotation);
        }
    }

    public class AOEShapeCone : AOEShape
    {
        public float Radius;
        public float DirectionOffset;
        public float HalfAngle;

        public AOEShapeCone(float radius, float halfAngle, float directionOffset = 0)
        {
            Radius = radius;
            DirectionOffset = directionOffset;
            HalfAngle = halfAngle;
        }

        public override bool Check(Vector3 position, Vector3 origin, float rotation)
        {
            var off = position - origin;
            return GeometryUtils.PointInCircle(off, Radius) && GeometryUtils.PointInCone(off, rotation + DirectionOffset, HalfAngle);
        }

        public override void Draw(MiniArena arena, Vector3 origin, float rotation)
        {
            arena.ZoneCone(origin, 0, Radius, rotation + DirectionOffset - HalfAngle, rotation + DirectionOffset + HalfAngle, arena.ColorAOE);
        }
    }

    public class AOEShapeCircle : AOEShape
    {
        public float Radius;

        public AOEShapeCircle(float radius)
        {
            Radius = radius;
        }

        public override bool Check(Vector3 position, Vector3 origin, float rotation)
        {
            return GeometryUtils.PointInCircle(position - origin, Radius);
        }

        public override void Draw(MiniArena arena, Vector3 origin, float rotation)
        {
            arena.ZoneCircle(origin, Radius, arena.ColorAOE);
        }
    }

    public class AOEShapeDonut : AOEShape
    {
        public float InnerRadius;
        public float OuterRadius;

        public AOEShapeDonut(float innerRadius, float outerRadius)
        {
            InnerRadius = innerRadius;
            OuterRadius = outerRadius;
        }

        public override bool Check(Vector3 position, Vector3 origin, float rotation)
        {
            var off = position - origin;
            return GeometryUtils.PointInCircle(off, OuterRadius) && !GeometryUtils.PointInCircle(off, InnerRadius);
        }

        public override void Draw(MiniArena arena, Vector3 origin, float rotation)
        {
            arena.ZoneDonut(origin, InnerRadius, OuterRadius, arena.ColorAOE);
        }
    }

    public class AOEShapeRect : AOEShape
    {
        public float LengthFront;
        public float LengthBack;
        public float HalfWidth;
        public float DirectionOffset;

        public AOEShapeRect(float lengthFront, float halfWidth, float lengthBack = 0, float directionOffset = 0)
        {
            LengthFront = lengthFront;
            LengthBack = lengthBack;
            HalfWidth = halfWidth;
            DirectionOffset = directionOffset;
        }

        public override bool Check(Vector3 position, Vector3 origin, float rotation)
        {
            return GeometryUtils.PointInRect(position - origin, rotation + DirectionOffset, LengthFront, LengthBack, HalfWidth);
        }

        public override void Draw(MiniArena arena, Vector3 origin, float rotation)
        {
            arena.ZoneQuad(origin, rotation + DirectionOffset, LengthFront, LengthBack, HalfWidth, arena.ColorAOE);
        }

        public void SetEndPoint(Vector3 endpoint, Vector3 origin, float rotation)
        {
            // this is a bit of a hack, but whatever...
            var dir = endpoint - origin;
            LengthFront = dir.Length();
            DirectionOffset = GeometryUtils.DirectionFromVec3(dir) - rotation;
        }

        public void SetEndPointFromCastLocation(Actor caster)
        {
            SetEndPoint(caster.CastInfo?.Location ?? new(), caster.Position, caster.Rotation);
        }
    }
}
