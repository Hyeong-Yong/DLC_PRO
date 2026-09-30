using DLC_PRO.ViewModels.Wavemeter;

namespace DLC_PRO.Interfaces {
    /// <summary>
    /// 메인 창과 별도인 보조 창 관리 (ViewModel은 창 객체를 직접 만들지 않는다).
    /// </summary>
    public interface IWindowService {
        /// <summary>WLM LongTerm graph 창을 연다. 이미 열려 있으면 앞으로 가져온다.</summary>
        void ShowLongTerm(LongTermViewModel viewModel);

        /// <summary>LongTerm 창이 열려 있는지.</summary>
        bool IsLongTermOpen { get; }

        /// <summary>LongTerm 창 닫기.</summary>
        void CloseLongTerm();
    }
}
