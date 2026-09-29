namespace DLC_PRO.Models {
    /// <summary>상태 표시등 색 (ViewModel은 상태만 정하고 실제 색은 View의 LedBrushConverter가 정한다).</summary>
    public enum LedState {
        /// <summary>꺼짐 / 알 수 없음</summary>
        Off,
        /// <summary>정상/ON (녹색)</summary>
        On,
        /// <summary>진행 중/주의 (주황)</summary>
        Warn,
        /// <summary>오류/열림 (빨강)</summary>
        Error,
        /// <summary>보류/정보 (파랑)</summary>
        Info,
        /// <summary>레이저 발광 (노랑)</summary>
        Emission,
    }
}
