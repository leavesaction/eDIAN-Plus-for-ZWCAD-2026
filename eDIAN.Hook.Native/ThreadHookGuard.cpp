#include "ThreadHookGuard.h"

namespace PhantomVfs {

thread_local bool ThreadHookGuard::s_inHook = false;

} // namespace PhantomVfs
