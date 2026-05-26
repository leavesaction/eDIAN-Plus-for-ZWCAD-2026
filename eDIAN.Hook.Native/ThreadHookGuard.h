#pragma once
#include <Windows.h>

namespace PhantomVfs {

/**
 * @brief 스레드별 훅 재진입 방지 관리 클래스
 */
class ThreadHookGuard {
public:
    /** @brief 현재 스레드가 훅 콜백 내부를 실행 중인지 여부를 반환합니다. */
    static bool IsBypassed() {
        return s_inHook;
    }

    /** @brief 현재 스레드의 훅 우회 상태를 수동으로 설정합니다. */
    static void SetBypassed(bool bypass) {
        s_inHook = bypass;
    }

private:
    static thread_local bool s_inHook;
};

/**
 * @brief RAII 기반의 안전한 훅 우회 가더 클래스
 */
class HookBypassGuard {
public:
    HookBypassGuard() {
        m_prevStatus = ThreadHookGuard::IsBypassed();
        ThreadHookGuard::SetBypassed(true);
    }

    ~HookBypassGuard() {
        ThreadHookGuard::SetBypassed(m_prevStatus);
    }

private:
    bool m_prevStatus;
};

} // namespace PhantomVfs
