#include "Internal.h"

namespace radha {

BridgeState& State() {
    static BridgeState state;
    return state;
}

} // namespace radha
