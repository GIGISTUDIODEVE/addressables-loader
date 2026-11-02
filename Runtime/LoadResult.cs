using System;

namespace AddressablesWork.Loader
{
    /// <summary>실패 종류 (R8-5). 재시도는 Network 에만 의미가 있다.</summary>
    public enum LoadFailure
    {
        None,
        /// <summary>카탈로그·번들을 받지 못함 (서버 없음, 연결 끊김, HTTP 차단)</summary>
        Network,
        /// <summary>카탈로그에 그 주소가 없음 (InvalidKeyException)</summary>
        KeyNotFound,
        /// <summary>번들은 찾았지만 내려받기·열기 실패</summary>
        Download,
        Unknown
    }

    public readonly struct LoadResult
    {
        public readonly LoadFailure Failure;
        public readonly string Message;
        public readonly Exception Exception;

        public bool Succeeded => Failure == LoadFailure.None;

        public LoadResult(LoadFailure failure, string message, Exception exception = null)
        {
            Failure = failure;
            Message = message;
            Exception = exception;
        }

        public static readonly LoadResult Ok = new LoadResult(LoadFailure.None, string.Empty);

        public override string ToString() => Succeeded ? "OK" : $"{Failure}: {Message}";
    }

    public readonly struct LoadResult<T>
    {
        public readonly T Value;
        public readonly LoadResult Status;

        public bool Succeeded => Status.Succeeded;

        public LoadResult(T value)
        {
            Value = value;
            Status = LoadResult.Ok;
        }

        public LoadResult(LoadResult failure)
        {
            Value = default;
            Status = failure;
        }

        public override string ToString() => Status.ToString();
    }
}
