#include <windows.h>
#include <ocidl.h>
#include <roapi.h>
#include <tlhelp32.h>
#include <atomic>
#include <map>
#include <mutex>
#include <string>
#include <utility>
#include <vector>
#include "NativeProtocol.h"
#include "xaml_abi.h"
#include "xaml_diagnostics_abi.h"

// The DLL changes the real Explorer-owned TextBlock. A discrete native XAML
// animation supplies Text while active. Stop() removes that animation layer;
// the original binding/local value is never overwritten. Native networking
// continues to update underneath, including while the selected glyph is held.
// There are no overlay windows, replacement tray icons, registry edits,
// persistent installations, or network configuration writes.

using namespace XamlAbi;
namespace {
HINSTANCE g_module = nullptr;
const CLSID TapClass = {0x13b5ca96,0xbedc,0x4a5e,{0x9a,0x5a,0x4a,0x53,0x4b,0x16,0xdb,0xb3}};

void TraceStage(const wchar_t* stage, HRESULT result = S_OK) noexcept {
#if !defined(NETWORK_ICON_DIAGNOSTICS_TRACE)
    // Release builds only retain actionable errors, not periodic diagnostics
    // or normal process/module lifecycle events.
    if (SUCCEEDED(result)) return;
#endif
    wchar_t path[MAX_PATH];
    DWORD size = GetTempPathW(ARRAYSIZE(path), path);
    if (!size || size + 35 >= ARRAYSIZE(path)) return;
    wcscat_s(path, L"TimestampConverter.Native.log");
    wchar_t line[512];
    swprintf_s(line, L"%llu PID=%lu TID=%lu %s hr=0x%08lX\r\n", GetTickCount64(), GetCurrentProcessId(), GetCurrentThreadId(), stage, static_cast<unsigned long>(result));
    char bytes[1536];
    int length = WideCharToMultiByte(CP_UTF8, 0, line, -1, bytes, sizeof(bytes), nullptr, nullptr);
    if (length <= 1) return;
    HANDLE file = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return;
    DWORD written;
    WriteFile(file, bytes, length - 1, &written, nullptr);
    CloseHandle(file);
}

template<class T> class ComPtr {
    T* value = nullptr;
public:
    ComPtr() = default;
    ~ComPtr() { reset(); }
    ComPtr(const ComPtr&) = delete;
    ComPtr& operator=(const ComPtr&) = delete;
    ComPtr(ComPtr&& other) noexcept : value(other.value) { other.value = nullptr; }
    ComPtr& operator=(ComPtr&& other) noexcept {
        if (this != &other) { reset(); value = other.value; other.value = nullptr; }
        return *this;
    }
    void reset() { if (value) { value->Release(); value = nullptr; } }
    T** put() { reset(); return &value; }
    T* get() const { return value; }
    T* operator->() const { return value; }
    explicit operator bool() const { return value != nullptr; }
};

class HString {
public:
    HSTRING value = nullptr;
    HString() = default;
    explicit HString(const std::wstring& text) {
        HRESULT hr = WindowsCreateString(text.c_str(), static_cast<UINT32>(text.size()), &value);
        if (FAILED(hr)) throw hr;
    }
    ~HString() { WindowsDeleteString(value); }
    HString(const HString&) = delete;
    HString& operator=(const HString&) = delete;
};

void Check(HRESULT hr) { if (FAILED(hr)) throw hr; }

wchar_t GlyphForMode(LONG mode) {
    if (mode == 1) return L'\uF384';
    if (mode == 2) return L'\uE839';
    if (mode == 3) return L'\uEC3F';
    return 0;
}

bool IsNetworkGlyph(wchar_t glyph) {
    return glyph == L'\uE701' || glyph == L'\uE709' || glyph == L'\uE7F4'
        || glyph == L'\uE839' || glyph == L'\uF384'
        || (glyph >= L'\uEC3C' && glyph <= L'\uEC3F')
        || (glyph >= L'\uE86C' && glyph <= L'\uE870')
        || (glyph >= L'\uF8C3' && glyph <= L'\uF8CC');
}

wchar_t ReadGlyph(ITextBlock* text) {
    HString current;
    Check(text->get_Text(&current.value));
    UINT32 length = 0;
    const wchar_t* data = WindowsGetStringRawBuffer(current.value, &length);
    return length == 1 ? data[0] : 0;
}

template<class T> void Factory(const wchar_t* runtimeClass, REFIID iid, ComPtr<T>& result) {
    HString className(runtimeClass);
    Check(RoGetActivationFactory(className.value, iid, reinterpret_cast<void**>(result.put())));
}

class Tap;
// Accessed only on the primary taskbar UI thread. Timers hold their own COM ref.
std::map<UINT_PTR, Tap*> g_timers;
std::mutex g_timersMutex;

struct Node {
    InstanceHandle parent = 0;
    std::wstring type;
    std::wstring name;
};

struct Target {
    ComPtr<ITextBlock> text;
    ComPtr<IDependencyObject> dependency;
    ComPtr<IStoryboard> storyboard;
    LONG mode = 0;
    wchar_t original = 0;
    bool mux = false;
};

class Tap final : public IObjectWithSite, public IVisualTreeServiceCallback2 {
    std::atomic<ULONG> references{1};
    ComPtr<IXamlDiagnostics> diagnostics;
    ComPtr<IVisualTreeService> tree;
    ComPtr<IXamlReaderStatics> readers[2];
    ComPtr<IStoryboardStatics> storyboardFactories[2];
    HANDLE mapping = nullptr;
    HANDLE ownerProcess = nullptr;
    NativeIconSharedState* shared = nullptr;
    DWORD taskbarThread = 0;
    UINT_PTR timer = 0;
    bool updating = false;
    std::atomic<bool> retired{false};
    LONG monitoredSequence = -1;
    ULONGLONG mismatchSince = 0;
    ULONGLONG attachedAt = 0;
    std::map<InstanceHandle, Node> nodes;
    std::mutex nodesMutex;
    std::vector<InstanceHandle> removedHandles;
    std::map<InstanceHandle, Target> targets;

    ~Tap() {
        // Target animations are stopped and released on their UI thread before
        // retiring. The diagnostics site can retain this COM object afterward.
        if (shared) UnmapViewOfFile(shared);
        if (mapping) CloseHandle(mapping);
        if (ownerProcess) CloseHandle(ownerProcess);
    }

    void Publish(LONG status, HRESULT error, const wchar_t* description, LONG sequence, LONG count, wchar_t current) {
        if (!shared) return;
        shared->nativeHeartbeat = GetTickCount64();
        shared->nativePid = GetCurrentProcessId();
        shared->matchedCount = count;
        shared->currentGlyph = current;
        shared->lastError = error;
        wcsncpy_s(shared->errorText, description ? description : L"", _TRUNCATE);
        InterlockedExchange(&shared->status, status);
        // Acknowledge only an applied/read-back state or an explicit failure.
        if (sequence >= 0) InterlockedExchange(&shared->ackSeq, sequence);
    }

    void Fail(HRESULT error, const wchar_t* description) {
        TraceStage(description, error);
        wchar_t detailed[160];
        swprintf_s(detailed, L"0x%08lX: %.130s", static_cast<unsigned long>(error), description);
        Publish(-1, error, detailed, shared ? shared->requestSeq : -1,
            shared ? shared->matchedCount : 0, 0);
    }

    bool IsInSystemTray(InstanceHandle handle) const {
        for (int depth = 0; handle && depth != 24; ++depth) {
            auto found = nodes.find(handle);
            if (found == nodes.end()) return false;
            const Node& node = found->second;
            if (node.type.compare(0, 11, L"SystemTray.") == 0) return true;
            handle = node.parent;
        }
        return false;
    }

    void DiscoverTargets() {
        std::vector<InstanceHandle> possible;
        {
            std::lock_guard<std::mutex> guard(nodesMutex);
            for (const auto& entry : nodes) {
                if (entry.second.name == L"InnerTextBlock" &&
                    entry.second.type.find(L"TextBlock") != std::wstring::npos &&
                    !targets.count(entry.first) && IsInSystemTray(entry.first))
                    possible.push_back(entry.first);
            }
        }
        for (InstanceHandle handle : possible) {
            ComPtr<IInspectable> element;
            if (FAILED(diagnostics->GetIInspectableFromHandle(handle, element.put()))) continue;
            Target target;
            HRESULT textResult = element->QueryInterface(IID_ITextBlock, reinterpret_cast<void**>(target.text.put()));
            if (FAILED(textResult)) {
                target.mux = true;
                if (FAILED(element->QueryInterface(IID_MuxITextBlock, reinterpret_cast<void**>(target.text.put())))) continue;
            }
            wchar_t glyph = ReadGlyph(target.text.get());
            if (!IsNetworkGlyph(glyph)) continue;
            Check(element->QueryInterface(target.mux ? IID_MuxIDependencyObject : IID_IDependencyObject, reinterpret_cast<void**>(target.dependency.put())));
            target.original = glyph;
            if (shared->originalGlyph == 0) shared->originalGlyph = glyph;
            targets.emplace(handle, std::move(target));
        }
    }

    void StopTarget(Target& target) {
        if (target.storyboard) {
            Check(target.storyboard->Stop());
            target.storyboard.reset();
        }
        target.mode = 0;
    }

    HRESULT RestoreAll() {
        HRESULT firstFailure = S_OK;
        for (auto& entry : targets) {
            try { StopTarget(entry.second); }
            catch (HRESULT hr) { if (SUCCEEDED(firstFailure)) firstFailure = hr; }
        }
        return firstFailure;
    }

    void Apply(Target& target, LONG mode) {
        if (target.mode == mode && target.storyboard &&
            ReadGlyph(target.text.get()) == GlyphForMode(mode)) return;
        StopTarget(target);
        auto& reader = readers[target.mux ? 1 : 0];
        auto& storyboardStatics = storyboardFactories[target.mux ? 1 : 0];
        if (!reader) Factory(target.mux ? L"Microsoft.UI.Xaml.Markup.XamlReader" : L"Windows.UI.Xaml.Markup.XamlReader",
            target.mux ? IID_MuxIXamlReaderStatics : IID_IXamlReaderStatics, reader);
        if (!storyboardStatics) Factory(target.mux ? L"Microsoft.UI.Xaml.Media.Animation.Storyboard" : L"Windows.UI.Xaml.Media.Animation.Storyboard",
            target.mux ? IID_MuxIStoryboardStatics : IID_IStoryboardStatics, storyboardStatics);

        wchar_t codePoint[16];
        swprintf_s(codePoint, L"%04X", static_cast<unsigned>(GlyphForMode(mode)));
        std::wstring markup =
            L"<Storyboard xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" "
            L"xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" FillBehavior=\"HoldEnd\">"
            L"<ObjectAnimationUsingKeyFrames Duration=\"0:0:1\" Storyboard.TargetProperty=\"Text\" EnableDependentAnimation=\"True\">"
            L"<DiscreteObjectKeyFrame KeyTime=\"0\"><DiscreteObjectKeyFrame.Value><x:String>&#x";
        markup += codePoint;
        markup += L";</x:String></DiscreteObjectKeyFrame.Value></DiscreteObjectKeyFrame></ObjectAnimationUsingKeyFrames></Storyboard>";
        HString xaml(markup);
        ComPtr<IInspectable> created;
        Check(reader->Load(xaml.value, created.put()));
        ComPtr<IStoryboard> storyboard;
        Check(created->QueryInterface(target.mux ? IID_MuxIStoryboard : IID_IStoryboard, reinterpret_cast<void**>(storyboard.put())));
        ComPtr<IVectorTimeline> children;
        Check(storyboard->get_Children(children.put()));
        ComPtr<ITimeline> animation;
        Check(children->GetAt(0, animation.put()));
        Check(storyboardStatics->SetTarget(animation.get(), target.dependency.get()));
        Check(storyboard->Begin());
        // Retain immediately so failure after Begin can always be rolled back.
        target.storyboard = std::move(storyboard);
        Check(target.storyboard->SeekAlignedToLastTick(TimeSpan{0}));
        target.mode = mode;
    }

    static DWORD WINAPI AdviseWorker(void* context) {
        Tap* self = static_cast<Tap*>(context);
        HRESULT hr = self->tree->AdviseVisualTreeChange(self);
        if (FAILED(hr)) self->Fail(hr, L"XAML diagnostics refused the visual-tree subscription.");
        self->Release();
        return 0;
    }

    static DWORD WINAPI UnadviseWorker(void* context) {
        Tap* self = static_cast<Tap*>(context);
        HRESULT hr = self->tree->UnadviseVisualTreeChange(self);
        if (FAILED(hr)) OutputDebugStringW(L"TimestampConverter: XAML diagnostics unsubscribe failed. Native animations are already stopped.\n");
        self->Release();
        return 0;
    }

    void Retire() {
        retired = true;
        if (timer) {
            KillTimer(nullptr, timer);
            { std::lock_guard<std::mutex> guard(g_timersMutex); g_timers.erase(timer); }
            timer = 0;
            Release(); // timer reference; Tick/OnVisualTreeChange retain theirs.
        }
        targets.clear();
        { std::lock_guard<std::mutex> guard(nodesMutex); nodes.clear(); removedHandles.clear(); }
        for (auto& reader : readers) reader.reset();
        for (auto& factory : storyboardFactories) factory.reset();
        AddRef();
        HANDLE worker = CreateThread(nullptr, 0, UnadviseWorker, this, 0, nullptr);
        if (worker) CloseHandle(worker); else Release();
    }

    static void CALLBACK TimerProc(HWND, UINT, UINT_PTR id, DWORD) {
        Tap* self;
        {
            std::lock_guard<std::mutex> guard(g_timersMutex);
            auto found = g_timers.find(id);
            if (found == g_timers.end()) return;
            self = found->second;
            self->AddRef();
        }
        self->Tick();
        self->Release();
    }

    void Tick() noexcept {
        if (updating || retired || !shared) return;
        updating = true;
        try {
            std::vector<InstanceHandle> removed;
            { std::lock_guard<std::mutex> guard(nodesMutex); removed.swap(removedHandles); }
            for (InstanceHandle handle : removed) {
                auto found = targets.find(handle);
                if (found != targets.end()) {
                    // A removed element is no longer displayed. Closed XAML
                    // objects can reject Stop; still release this stale target
                    // so it cannot poison later apply/restore operations.
                    try { StopTarget(found->second); } catch (HRESULT) {}
                    targets.erase(found);
                }
            }
            ULONGLONG now = GetTickCount64();
            ULONGLONG heartbeat = shared->heartbeat;
            bool ownerAlive = ownerProcess && WaitForSingleObject(ownerProcess, 0) == WAIT_TIMEOUT;
            if (!ownerAlive || heartbeat > now + 1000 || (now > heartbeat && now - heartbeat > 5000)) {
                HRESULT hr = RestoreAll();
                Publish(SUCCEEDED(hr) ? -2 : -1, hr, L"Controller stopped; native icon restored.", shared->requestSeq,
                    static_cast<LONG>(targets.size()), targets.empty() ? 0 : ReadGlyph(targets.begin()->second.text.get()));
                if (SUCCEEDED(hr)) Retire();
                updating = false;
                return;
            }
            LONG sequence = InterlockedCompareExchange(&shared->requestSeq, 0, 0);
            LONG mode = shared->mode;
            MemoryBarrier();
            if (sequence != shared->requestSeq) { updating = false; return; }
            if (sequence != monitoredSequence) {
                monitoredSequence = sequence;
                mismatchSince = 0;
            }
            if (mode < 0 || mode > 3) throw E_INVALIDARG;
            DiscoverTargets();
            if (targets.empty()) {
                // A process may expose several XAML cores. An empty/non-taskbar
                // core must not claim or overwrite the controller's handshake.
                if (now - attachedAt > 5000) Retire();
                updating = false;
                return;
            }
            if (mode == 0) {
                Check(RestoreAll());
                wchar_t current = targets.empty() ? 0 : ReadGlyph(targets.begin()->second.text.get());
                Publish(1, S_OK, L"", sequence, static_cast<LONG>(targets.size()), current);
                mismatchSince = 0;
            } else {
                for (auto& entry : targets) Apply(entry.second, mode);
                wchar_t expected = GlyphForMode(mode);
                bool verified = true;
                for (auto& entry : targets) {
                    if (ReadGlyph(entry.second.text.get()) != expected) verified = false;
                }
                if (verified) {
                    Publish(2, S_OK, L"", sequence, static_cast<LONG>(targets.size()), expected);
                    mismatchSince = 0;
                } else {
                    // A native visual state change can replace an animation
                    // after an earlier success. Apply retries on the next tick;
                    // publish waiting immediately and bound retries for both a
                    // new request and an already acknowledged request.
                    if (!mismatchSince) mismatchSince = now;
                    Publish(0, S_OK, L"Waiting for native glyph readback.", -1,
                        static_cast<LONG>(targets.size()), ReadGlyph(targets.begin()->second.text.get()));
                    if (now - mismatchSince > 5000) {
                        Check(RestoreAll());
                        Fail(E_FAIL, L"Native XAML did not expose the requested glyph after animation; changes were restored.");
                        mismatchSince = 0;
                    }
                }
            }
        } catch (HRESULT hr) {
            RestoreAll();
            Fail(hr, L"Native XAML operation failed; animation overrides were removed.");
        } catch (...) {
            RestoreAll();
            Fail(E_FAIL, L"Unexpected native XAML error; animation overrides were removed.");
        }
        updating = false;
    }

public:
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** result) override {
        if (!result) return E_POINTER;
        *result = nullptr;
        if (iid == IID_IUnknown || iid == IID_IObjectWithSite) *result = static_cast<IObjectWithSite*>(this);
        else if (iid == IID_IVisualTreeServiceCallback || iid == IID_IVisualTreeServiceCallback2)
            *result = static_cast<IVisualTreeServiceCallback2*>(this);
        else return E_NOINTERFACE;
        AddRef();
        return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++references; }
    ULONG STDMETHODCALLTYPE Release() override {
        ULONG remaining = --references;
        if (!remaining) delete this;
        return remaining;
    }

    HRESULT STDMETHODCALLTYPE SetSite(IUnknown* site) override {
        TraceStage(L"SetSite entered");
        if (!site) return S_OK;
        if (diagnostics) return HRESULT_FROM_WIN32(ERROR_ALREADY_INITIALIZED);
        try {
            Check(site->QueryInterface(IID_IXamlDiagnostics, reinterpret_cast<void**>(diagnostics.put())));
            Check(site->QueryInterface(IID_IVisualTreeService, reinterpret_cast<void**>(tree.put())));
            TraceStage(L"SetSite obtained diagnostics/tree");
            BSTR initialization = nullptr;
            Check(diagnostics->GetInitializationData(&initialization));
            std::wstring mappingName = initialization ? initialization : L"";
            SysFreeString(initialization);
            if (mappingName.compare(0, 6, L"Local\\") != 0) throw E_INVALIDARG;
            mapping = OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE, FALSE, mappingName.c_str());
            if (!mapping) throw HRESULT_FROM_WIN32(GetLastError());
            shared = static_cast<NativeIconSharedState*>(MapViewOfFile(mapping, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, sizeof(NativeIconSharedState)));
            if (!shared) throw HRESULT_FROM_WIN32(GetLastError());
            if (shared->magic != NativeIconMagic || shared->version != NativeIconVersion) throw E_INVALIDARG;
            DWORD taskbarPid = 0;
            taskbarThread = GetWindowThreadProcessId(FindWindowW(L"Shell_TrayWnd", nullptr), &taskbarPid);
            if (!taskbarThread || taskbarPid != GetCurrentProcessId()) throw E_INVALIDARG;
            wchar_t threadDescription[128];
            swprintf_s(threadDescription, L"SetSite UI=%lu; Shell_TrayWnd UI=%lu", GetCurrentThreadId(), taskbarThread);
            TraceStage(threadDescription);
            if (GetCurrentThreadId() != taskbarThread) {
                TraceStage(L"Skipping a non-taskbar XAML core");
                return RPC_E_WRONG_THREAD;
            }
            ownerProcess = OpenProcess(SYNCHRONIZE, FALSE, shared->ownerPid);
            if (!ownerProcess) throw HRESULT_FROM_WIN32(GetLastError());
            attachedAt = GetTickCount64();
            TraceStage(L"SetSite mapped control channel");
            // SetSite is dispatched on the XAML UI thread. The initial visual
            // tree replay may instead call our callback on the Advise worker.
            // Store only metadata there; every native object access happens on
            // this UI-thread timer, after SetSite has returned to its pump.
            timer = SetTimer(nullptr, 0, 500, TimerProc);
            if (!timer) throw HRESULT_FROM_WIN32(GetLastError());
            AddRef();
            { std::lock_guard<std::mutex> guard(g_timersMutex); g_timers.emplace(timer, this); }
            // Keep this small module mapped until Explorer exits. This avoids a
            // use-after-unload from XAML's COM caches; stopped sessions are inert.
            HMODULE pinned = nullptr;
            GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
                reinterpret_cast<LPCWSTR>(&g_module), &pinned);
            AddRef();
            HANDLE worker = CreateThread(nullptr, 0, AdviseWorker, this, 0, nullptr);
            if (!worker) { Release(); throw HRESULT_FROM_WIN32(GetLastError()); }
            CloseHandle(worker);
            return S_OK;
        } catch (HRESULT hr) { Fail(hr, L"Failed to initialize the native XAML session."); return hr; }
        catch (...) { Fail(E_FAIL, L"Failed to initialize the native XAML session."); return E_FAIL; }
    }

    HRESULT STDMETHODCALLTYPE GetSite(REFIID iid, void** result) override {
        if (!result) return E_POINTER;
        *result = nullptr;
        return diagnostics ? diagnostics->QueryInterface(iid, result) : E_FAIL;
    }

    HRESULT STDMETHODCALLTYPE OnVisualTreeChange(ParentChildRelation relation, VisualElement element, VisualMutationType mutation) override {
        if (retired) return S_OK;
        try {
            std::lock_guard<std::mutex> guard(nodesMutex);
            if (nodes.empty()) TraceStage(L"Received visual tree callback");
            if (mutation == XamlAbi::Add) {
                if (nodes.size() > 50000) throw HRESULT_FROM_WIN32(ERROR_BUFFER_OVERFLOW);
                nodes[element.Handle] = Node{relation.Parent, element.Type ? element.Type : L"", element.Name ? element.Name : L""};
            } else {
                removedHandles.push_back(element.Handle);
                nodes.erase(element.Handle);
            }
            return S_OK;
        } catch (HRESULT hr) { Fail(hr, L"Failed to inspect native taskbar elements."); return hr; }
        catch (...) { Fail(E_FAIL, L"Failed to inspect native taskbar elements."); return E_FAIL; }
    }

    HRESULT STDMETHODCALLTYPE OnElementStateChanged(InstanceHandle, VisualElementState, LPCWSTR) override { return S_OK; }
};

class TapFactory final : public IClassFactory {
    std::atomic<ULONG> references{1};
public:
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** result) override {
        if (!result) return E_POINTER;
        *result = nullptr;
        if (iid != IID_IUnknown && iid != IID_IClassFactory) return E_NOINTERFACE;
        *result = static_cast<IClassFactory*>(this); AddRef(); return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++references; }
    ULONG STDMETHODCALLTYPE Release() override { ULONG count = --references; if (!count) delete this; return count; }
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID iid, void** result) override {
        if (outer) return CLASS_E_NOAGGREGATION;
        if (!result) return E_POINTER;
        *result = nullptr;
        try { Tap* tap = new Tap(); HRESULT hr = tap->QueryInterface(iid, result); tap->Release(); return hr; }
        catch (...) { return E_OUTOFMEMORY; }
    }
    HRESULT STDMETHODCALLTYPE LockServer(BOOL) override { return S_OK; }
};
} // namespace

extern "C" HRESULT WINAPI DllGetClassObject(REFCLSID clsid, REFIID iid, void** result) {
    TraceStage(L"DllGetClassObject entered");
    if (!result) return E_POINTER;
    *result = nullptr;
    if (clsid != TapClass) return CLASS_E_CLASSNOTAVAILABLE;
    try { TapFactory* factory = new TapFactory(); HRESULT hr = factory->QueryInterface(iid, result); factory->Release(); return hr; }
    catch (...) { return E_OUTOFMEMORY; }
}

extern "C" HRESULT WINAPI DllCanUnloadNow() { return S_FALSE; }

static HRESULT InitializeDiagnostics(DWORD explorerPid, LPCWSTR mappingName) {
    TraceStage(L"Native diagnostics initializer entered");
    if (!mappingName || wcslen(mappingName) > 240 || wcsncmp(mappingName, L"Local\\", 6) != 0) return E_INVALIDARG;
    DWORD actualPid = 0;
    if (!GetWindowThreadProcessId(FindWindowW(L"Shell_TrayWnd", nullptr), &actualPid) || explorerPid != actualPid)
        return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
    HRESULT com = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    TraceStage(L"NativeAttach CoInitializeEx", com);
    struct ComCleanup { bool initialized; ~ComCleanup() { if (initialized) CoUninitialize(); } } comCleanup{SUCCEEDED(com)};
    wchar_t dllPath[32768];
    DWORD pathLength = GetModuleFileNameW(g_module, dllPath, ARRAYSIZE(dllPath));
    if (!pathLength || pathLength == ARRAYSIZE(dllPath)) return HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER);
    TraceStage(dllPath);
    HANDLE controlMapping = OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE, FALSE, mappingName);
    if (!controlMapping) return HRESULT_FROM_WIN32(GetLastError());
    auto control = static_cast<NativeIconSharedState*>(MapViewOfFile(controlMapping, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, sizeof(NativeIconSharedState)));
    if (!control) { CloseHandle(controlMapping); return HRESULT_FROM_WIN32(GetLastError()); }
    struct MappingCleanup { NativeIconSharedState* view; HANDLE handle; ~MappingCleanup() { UnmapViewOfFile(view); CloseHandle(handle); } } mappingCleanup{control, controlMapping};

    std::wstring winuiFramework;
    std::wstring winuiXaml;
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE, explorerPid);
    if (snapshot != INVALID_HANDLE_VALUE) {
        MODULEENTRY32W module{sizeof(module)};
        if (Module32FirstW(snapshot, &module)) do {
            if (_wcsicmp(module.szModule, L"Microsoft.Internal.FrameworkUdk.dll") == 0) winuiFramework = module.szExePath;
            if (_wcsicmp(module.szModule, L"Microsoft.UI.Xaml.dll") == 0) winuiXaml = module.szExePath;
        } while (Module32NextW(snapshot, &module));
        CloseHandle(snapshot);
    }
    HRESULT hr = HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
    ULONGLONG wholeScanStart = GetTickCount64();
    // The system exposes numbered diagnostic endpoints; API examples use this
    // discovery because the endpoint is not returned by a public enumeration.
    for (int framework = 0; framework != 2; ++framework) {
        HMODULE xaml = framework == 0
            ? LoadLibraryExW(L"Windows.UI.Xaml.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32)
            : (winuiFramework.empty() ? nullptr : LoadLibraryExW(winuiFramework.c_str(), nullptr, LOAD_WITH_ALTERED_SEARCH_PATH));
        if (!xaml) { hr = HRESULT_FROM_WIN32(GetLastError()); continue; }
        auto initialize = reinterpret_cast<InitializeXamlDiagnosticsExFn>(GetProcAddress(xaml, "InitializeXamlDiagnosticsEx"));
        if (!initialize) { hr = HRESULT_FROM_WIN32(GetLastError()); continue; }
        wchar_t systemXamlPath[32768];
        if (!GetModuleFileNameW(xaml, systemXamlPath, ARRAYSIZE(systemXamlPath))) { hr = HRESULT_FROM_WIN32(GetLastError()); continue; }
        const wchar_t* diagnosticXaml = framework == 0 ? systemXamlPath : winuiXaml.c_str();
        if (!diagnosticXaml[0]) { hr = HRESULT_FROM_WIN32(ERROR_MOD_NOT_FOUND); continue; }
        TraceStage(diagnosticXaml);
        ULONGLONG start = GetTickCount64();
        for (unsigned index = 1; index <= 10000 && GetTickCount64() - start < 4500 && GetTickCount64() - wholeScanStart < 8500; ++index) {
            if (control->nativePid == static_cast<LONG>(explorerPid) && control->matchedCount > 0) return S_OK;
            wchar_t endpoint[64];
            swprintf_s(endpoint, framework == 0 ? L"VisualDiagConnection%u" : L"WinUIVisualDiagConnection%u", index);
            hr = initialize(endpoint, explorerPid, diagnosticXaml, dllPath, TapClass, mappingName);
            if (hr == HRESULT_FROM_WIN32(ERROR_NOT_FOUND)) continue;
            TraceStage(endpoint, hr);
            if (SUCCEEDED(hr)) {
                // The initializer queues onto a particular core's dispatcher
                // and can return S_OK even when that core never processes it.
                // Accept only a real TAP/network-element acknowledgement.
                for (int wait = 0; wait < 14; ++wait) {
                    if (control->nativePid == static_cast<LONG>(explorerPid) && control->matchedCount > 0) return S_OK;
                    Sleep(50);
                }
                TraceStage(L"Endpoint has no native network-element acknowledgement", hr);
                hr = HRESULT_FROM_WIN32(ERROR_TIMEOUT);
                continue;
            }
            break;
        }
    }
    return hr;
}

extern "C" __declspec(dllexport) HRESULT WINAPI NativeAttach(DWORD explorerPid, LPCWSTR mappingName) {
    TraceStage(L"NativeAttach direct diagnostics entry");
    if (!mappingName || wcslen(mappingName) > 240 || wcsncmp(mappingName, L"Local\\TimestampConverter.Native.", 32) != 0) return E_INVALIDARG;
    HWND taskbar = FindWindowW(L"Shell_TrayWnd", nullptr);
    DWORD actualPid = 0;
    DWORD thread = GetWindowThreadProcessId(taskbar, &actualPid);
    if (!thread || actualPid != explorerPid) return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
    HANDLE mapping = OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE, FALSE, mappingName);
    if (!mapping) return HRESULT_FROM_WIN32(GetLastError());
    auto shared = static_cast<NativeIconSharedState*>(MapViewOfFile(mapping, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, sizeof(NativeIconSharedState)));
    if (!shared) { CloseHandle(mapping); return HRESULT_FROM_WIN32(GetLastError()); }
    struct ControlCleanup { NativeIconSharedState* view; HANDLE mapping; ~ControlCleanup() { UnmapViewOfFile(view); CloseHandle(mapping); } } cleanup{shared, mapping};
    if (shared->magic != NativeIconMagic || shared->version != NativeIconVersion || shared->ownerPid != GetCurrentProcessId()) return E_INVALIDARG;

    // The documented diagnostics initializer loads the TAP into the selected
    // XAML core. Prefer it directly; no taskbar message hook is needed when the
    // extracted module is accessible at its application-local path.
    return InitializeDiagnostics(explorerPid, mappingName);


}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) { g_module = instance; DisableThreadLibraryCalls(instance); }
    return TRUE;
}
