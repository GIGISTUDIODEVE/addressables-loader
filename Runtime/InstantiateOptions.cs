using UnityEngine;

namespace AddressablesWork.Loader
{
    /// <summary>생성 옵션 (R8-6). 레이어는 소비자 정책이므로 여기서 일괄 지정한다 (R4-3).</summary>
    public struct InstantiateOptions
    {
        public Transform Parent;
        public Vector3 Position;
        public Quaternion Rotation;
        /// <summary>-1 이면 프리팹의 레이어(Default)를 그대로 둔다. 0 이상이면 모든 자식에 적용.</summary>
        public int Layer;

        public static InstantiateOptions Default => new InstantiateOptions
        {
            Parent = null,
            Position = Vector3.zero,
            Rotation = Quaternion.identity,
            Layer = -1
        };

        public static InstantiateOptions At(Vector3 position, Transform parent = null)
        {
            var o = Default;
            o.Position = position;
            o.Parent = parent;
            return o;
        }
    }
}
