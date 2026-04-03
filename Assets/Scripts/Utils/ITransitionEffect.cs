using System.Threading.Tasks;
using UnityEngine;

// 화면 전환 효과의 공통 인터페이스
public interface ITransitionEffect
{
    // 등장 애니메이션
    Task PlayEnterAsync(CanvasGroup target);

    // 퇴장 애니메이션
    Task PlayExitAsync(CanvasGroup target);
}
