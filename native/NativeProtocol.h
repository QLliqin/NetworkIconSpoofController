#pragma once
#include <windows.h>
#include <cstddef>

// Shared with src/NativeNetworkIconController.cs. One mapping per controller.
// UTF-16 errorText is NUL terminated. All field offsets are fixed across x64
// and managed code; requestSeq/ackSeq are the publish/acknowledgement barriers.
struct alignas(8) NativeIconSharedState {
    DWORD magic;                       // 0: 0x54494332
    DWORD version;                     // 4: 1
    DWORD ownerPid;                    // 8
    volatile LONG mode;               // 12: default=0, USA=1, China=2, UK=3
    volatile ULONGLONG heartbeat;      // 16: GetTickCount64, controller-owned
    volatile LONG requestSeq;         // 24
    volatile LONG ackSeq;             // 28
    volatile LONG status;             // 32: waiting=0, restored=1, applied=2
                                      //     error=-1, owner lost=-2
    volatile LONG lastError;          // 36: HRESULT
    volatile LONG matchedCount;       // 40
    volatile LONG currentGlyph;       // 44: first actual network TextBlock
    volatile LONG originalGlyph;      // 48: native glyph seen before override
    volatile LONG nativePid;          // 52: must equal target Explorer pid
    volatile ULONGLONG nativeHeartbeat;// 56: GetTickCount64, DLL-owned
    WCHAR errorText[160];              // 64
};

static_assert(sizeof(NativeIconSharedState) == 384, "Shared protocol size changed");
static_assert(offsetof(NativeIconSharedState, heartbeat) == 16, "Bad heartbeat offset");
static_assert(offsetof(NativeIconSharedState, requestSeq) == 24, "Bad sequence offset");
static_assert(offsetof(NativeIconSharedState, errorText) == 64, "Bad error offset");

constexpr DWORD NativeIconMagic = 0x54494332;
constexpr DWORD NativeIconVersion = 1;
