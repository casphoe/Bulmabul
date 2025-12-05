namespace Gpm.Ui
{
    using UnityEngine;

    public partial class InfiniteScroll
    {
        public enum CurveType
        {
            EASE_IN_OUT = 0,
            LINEAR,
            EASE_IN,
            EASE_OUT
        }
        
        public class Curve
        {
            public Curve(AnimationCurve curve, float time)
            {
                this.curve = curve;
                this.time = time;
            }
            
            public AnimationCurve curve;
            public float time;
        }
        
        public class CurveFromType : Curve
        {
            public CurveFromType(CurveType curveType, float time) : base(GetAnimation(curveType), time)
            {
            }
            
            public static AnimationCurve GetAnimation(CurveType curveType)
            {
                switch (curveType)
                {
                    case CurveType.EASE_IN_OUT:
                        return AnimationCurve.EaseInOut(0, 0, 1, 1);
                    case CurveType.LINEAR:
                        return AnimationCurve.Linear(0, 0, 1, 1);
                    case CurveType.EASE_IN:
                        return EaseIn(0, 0, 1, 1);
                    case CurveType.EASE_OUT:
                        return EaseOut(0, 0, 1, 1);
                    default:
                        return AnimationCurve.EaseInOut(0, 0, 1, 1);
                }
            }

            public static AnimationCurve EaseIn(
                float timeStart,
                float valueStart,
                float timeEnd,
                float valueEnd)
            {
                if (Mathf.Approximately(timeStart, timeEnd))
                {
                    return new AnimationCurve(new Keyframe(timeStart, valueStart));
                }

                float num = (2*valueEnd - valueStart) / (timeEnd - timeStart);
                return new AnimationCurve(
                    new Keyframe(timeStart, valueStart, 0.0f, 0.0f),
                    new Keyframe(timeEnd, valueEnd, num, num));
            }
        
            public static AnimationCurve EaseOut(
                float timeStart,
                float valueStart,
                float timeEnd,
                float valueEnd)
            {
                if (Mathf.Approximately(timeStart, timeEnd))
                {
                    return new AnimationCurve(new Keyframe(timeStart, valueStart));
                }

                float num = ( 2*valueEnd - valueStart) / (timeEnd - timeStart);
                return new AnimationCurve(
                    new Keyframe(timeStart, valueStart, num, num),
                    new Keyframe(timeEnd, valueEnd, 0.0f, 0.0f)
                );
            }
        }
    }
}
