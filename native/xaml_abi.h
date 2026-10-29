#pragma once

#include <windows.h>
#include <inspectable.h>
#include <winstring.h>

// Win32's zero-argument compatibility macro collides with the XAML method.
#ifdef GetCurrentTime
#undef GetCurrentTime
#endif

// Minimal ABI declarations for Windows.UI.Xaml. These declarations follow the
// Windows SDK IDL, not a guessed interface layout. Source:
// https://github.com/microsoft/win32metadata/tree/main/generation/WinSDK/RecompiledIdlHeaders/winrt
// windows.ui.xaml.idl, windows.ui.xaml.controls.idl,
// windows.ui.xaml.markup.idl, windows.ui.xaml.media.animation.idl/.h
// Pointer-only interfaces intentionally expose only their IInspectable prefix.
namespace XamlAbi {

static const IID IID_IXamlReaderStatics = {0x9891c6bd,0x534f,0x4955,{0xb8,0x5a,0x8a,0x8d,0xc0,0xdc,0xa6,0x02}};
static const IID IID_IStoryboard = {0xd45c1e6e,0x3594,0x460e,{0x98,0x1a,0x32,0x27,0x1b,0xd3,0xaa,0x06}};
static const IID IID_IStoryboardStatics = {0xd82f07d8,0x73d5,0x4379,{0xbd,0x48,0x7e,0x05,0x18,0x4a,0x8b,0xad}};
static const IID IID_ITimeline = {0x0bc465dc,0xbe4d,0x4d0d,{0x95,0x49,0x22,0x08,0xb7,0x15,0xf4,0x0d}};
static const IID IID_IVectorTimeline = {0x2005c7f9,0xc7d7,0x521b,{0xa8,0x1c,0x2c,0x9d,0xad,0x77,0xa9,0xe8}};
static const IID IID_IDependencyObject = {0x5c526665,0xf60e,0x4912,{0xaf,0x59,0x5f,0xe0,0x68,0x0f,0x08,0x9d}};
static const IID IID_ITextBlock = {0xae2d9271,0x3b4a,0x45fc,{0x84,0x68,0xf7,0x94,0x95,0x48,0xf4,0xd5}};

// WinUI 3 uses different interface identities. These were verified against the
// Microsoft.UI.Xaml.winmd supplied by Windows build 28000 (CBS.2 runtime).
// IXamlReaderStatics, IStoryboard, IStoryboardStatics, ITimeline and the
// ITextBlock prefix below have the same method order in both frameworks.
// IDependencyObject matches ONLY through GetAnimationBaseValue: WinUI 3 inserts
// property callbacks before get_Dispatcher, so never call the UWP Dispatcher
// slot on a pointer obtained with IID_MuxIDependencyObject.
static const IID IID_MuxIXamlReaderStatics = {0x82a4cd9e,0x435e,0x5aeb,{0x8c,0x4f,0x30,0x0c,0xec,0xe4,0x5c,0xae}};
static const IID IID_MuxIStoryboard = {0x04d41bb3,0x8721,0x519e,{0x8e,0x53,0xfb,0x8b,0x34,0x92,0x03,0x05}};
static const IID IID_MuxIStoryboardStatics = {0xdd18519b,0xd4e4,0x597d,{0xa0,0xb7,0x65,0x5e,0xbd,0xd3,0x5e,0xfa}};
static const IID IID_MuxITimeline = {0xd0f9b330,0xcc2a,0x5b05,{0x97,0x86,0x2d,0xa4,0xc6,0x58,0x45,0x81}};
static const IID IID_MuxIVectorTimeline = {0x4107a612,0x6757,0x5552,{0xa4,0x8b,0x2e,0x30,0x93,0x23,0x0c,0x49}};
static const IID IID_MuxIDependencyObject = {0xe7beaee7,0x160e,0x50f7,{0x87,0x89,0xd6,0x34,0x63,0xf9,0x79,0xfa}};
static const IID IID_MuxITextBlock = {0x1ac8d84f,0x392c,0x5c7e,{0x83,0xf5,0xa5,0x3e,0x3b,0xf0,0xab,0xb0}};

struct ITimeline : IInspectable {};
struct IDependencyProperty : IInspectable {};
struct IVectorViewTimeline : IInspectable {};
struct IFontFamily : IInspectable {};
struct IBrush : IInspectable {};
struct ICoreDispatcher : IInspectable {};
struct TimeSpan { INT64 Duration; };
struct FontWeight { UINT16 Weight; };

struct IXamlReaderStatics : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE Load(HSTRING xaml, IInspectable** result) = 0;
    virtual HRESULT STDMETHODCALLTYPE LoadWithInitialTemplateValidation(HSTRING xaml, IInspectable** result) = 0;
};

struct IVectorTimeline : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE GetAt(UINT32 index, ITimeline** value) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_Size(UINT32* value) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetView(IVectorViewTimeline** value) = 0;
    virtual HRESULT STDMETHODCALLTYPE IndexOf(ITimeline* value, UINT32* index, boolean* found) = 0;
    virtual HRESULT STDMETHODCALLTYPE SetAt(UINT32 index, ITimeline* value) = 0;
    virtual HRESULT STDMETHODCALLTYPE InsertAt(UINT32 index, ITimeline* value) = 0;
    virtual HRESULT STDMETHODCALLTYPE RemoveAt(UINT32 index) = 0;
    virtual HRESULT STDMETHODCALLTYPE Append(ITimeline* value) = 0;
    virtual HRESULT STDMETHODCALLTYPE RemoveAtEnd() = 0;
    virtual HRESULT STDMETHODCALLTYPE Clear() = 0;
    virtual HRESULT STDMETHODCALLTYPE GetMany(UINT32 startIndex, UINT32 capacity, ITimeline** value, UINT32* actual) = 0;
    virtual HRESULT STDMETHODCALLTYPE ReplaceAll(UINT32 count, ITimeline** value) = 0;
};

struct IDependencyObject : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE GetValue(IDependencyProperty* dp, IInspectable** result) = 0;
    virtual HRESULT STDMETHODCALLTYPE SetValue(IDependencyProperty* dp, IInspectable* value) = 0;
    virtual HRESULT STDMETHODCALLTYPE ClearValue(IDependencyProperty* dp) = 0;
    virtual HRESULT STDMETHODCALLTYPE ReadLocalValue(IDependencyProperty* dp, IInspectable** result) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetAnimationBaseValue(IDependencyProperty* dp, IInspectable** result) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_Dispatcher(ICoreDispatcher** value) = 0;
};

struct IStoryboard : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE get_Children(IVectorTimeline** value) = 0;
    virtual HRESULT STDMETHODCALLTYPE Seek(TimeSpan offset) = 0;
    virtual HRESULT STDMETHODCALLTYPE Stop() = 0;
    virtual HRESULT STDMETHODCALLTYPE Begin() = 0;
    virtual HRESULT STDMETHODCALLTYPE Pause() = 0;
    virtual HRESULT STDMETHODCALLTYPE Resume() = 0;
    virtual HRESULT STDMETHODCALLTYPE GetCurrentState(INT32* result) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetCurrentTime(TimeSpan* result) = 0;
    virtual HRESULT STDMETHODCALLTYPE SeekAlignedToLastTick(TimeSpan offset) = 0;
    virtual HRESULT STDMETHODCALLTYPE SkipToFill() = 0;
};

struct IStoryboardStatics : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE get_TargetPropertyProperty(IDependencyProperty** value) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetTargetProperty(ITimeline* element, HSTRING* result) = 0;
    virtual HRESULT STDMETHODCALLTYPE SetTargetProperty(ITimeline* element, HSTRING path) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_TargetNameProperty(IDependencyProperty** value) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetTargetName(ITimeline* element, HSTRING* result) = 0;
    virtual HRESULT STDMETHODCALLTYPE SetTargetName(ITimeline* element, HSTRING name) = 0;
    virtual HRESULT STDMETHODCALLTYPE SetTarget(ITimeline* timeline, IDependencyObject* target) = 0;
};

// This is the exact ITextBlock ABI prefix through Text. The remaining SDK
// methods are deliberately not exposed. Font/style enum slots use INT32, their
// ABI representation. Do not substitute IInspectable* for a queried interface.
struct ITextBlock : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE get_FontSize(DOUBLE* value) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_FontSize(DOUBLE value) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_FontFamily(IFontFamily** value) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_FontFamily(IFontFamily* value) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_FontWeight(FontWeight* value) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_FontWeight(FontWeight value) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_FontStyle(INT32* value) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_FontStyle(INT32 value) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_FontStretch(INT32* value) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_FontStretch(INT32 value) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_CharacterSpacing(INT32* value) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_CharacterSpacing(INT32 value) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_Foreground(IBrush** value) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_Foreground(IBrush* value) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_TextWrapping(INT32* value) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_TextWrapping(INT32 value) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_TextTrimming(INT32* value) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_TextTrimming(INT32 value) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_TextAlignment(INT32* value) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_TextAlignment(INT32 value) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_Text(HSTRING* value) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_Text(HSTRING value) = 0;
};

} // namespace XamlAbi
