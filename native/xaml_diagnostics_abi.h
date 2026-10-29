#pragma once

#include <windows.h>
#include <oaidl.h>
#include <inspectable.h>

// Minimal declarations for the Windows XAML diagnostics COM API, following:
// https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/xamlOM.h
// Explicit GUIDs make this usable with the MinGW headers without SDK projection
// libraries. Struct layout and method order are the native Windows ABI.
namespace XamlAbi {

typedef UINT64 InstanceHandle;
enum VisualMutationType { Add = 0, Remove = 1 };
enum VisualElementState { ErrorResolved = 0, ErrorResourceNotFound = 1, ErrorInvalidResource = 2 };
enum BaseValueSource {
    BaseValueSourceUnknown = 0, BaseValueSourceDefault = 1,
    BaseValueSourceBuiltInStyle = 2, BaseValueSourceStyle = 3,
    BaseValueSourceLocal = 4, Inherited = 5, DefaultStyleTrigger = 6,
    TemplateTrigger = 7, StyleTrigger = 8, ImplicitStyleReference = 9,
    ParentTemplate = 10, ParentTemplateTrigger = 11, Animation = 12,
    Coercion = 13, BaseValueSourceVisualState = 14
};
enum MetadataBit {
    None = 0, IsValueHandle = 0x1, IsPropertyReadOnly = 0x2,
    IsValueCollection = 0x4, IsValueCollectionReadOnly = 0x8,
    IsValueBindingExpression = 0x10, IsValueNull = 0x20,
    IsValueHandleAndEvaluatedValue = 0x40
};

struct SourceInfo {
    BSTR FileName;
    UINT LineNumber;
    UINT ColumnNumber;
    UINT CharPosition;
    BSTR Hash;
};
struct ParentChildRelation {
    InstanceHandle Parent;
    InstanceHandle Child;
    UINT ChildIndex;
};
struct VisualElement {
    InstanceHandle Handle;
    SourceInfo SrcInfo;
    BSTR Type;
    BSTR Name;
    UINT NumChildren;
};
struct PropertyChainSource {
    InstanceHandle Handle;
    BSTR TargetType;
    BSTR Name;
    BaseValueSource Source;
    SourceInfo SrcInfo;
};
struct PropertyChainValue {
    UINT Index;
    BSTR Type;
    BSTR DeclaringType;
    BSTR ValueType;
    BSTR ItemType;
    BSTR Value;
    BOOL Overridden;
    INT64 MetadataBits;
    BSTR PropertyName;
    UINT PropertyChainIndex;
};
struct EnumType {
    BSTR Name;
    SAFEARRAY* ValueInts;
    SAFEARRAY* ValueStrings;
};
struct CollectionElementValue {
    UINT Index;
    BSTR ValueType;
    BSTR Value;
    INT64 MetadataBits;
};

#ifdef _WIN64
static_assert(sizeof(SourceInfo) == 32, "XAML SourceInfo ABI mismatch");
static_assert(sizeof(ParentChildRelation) == 24, "XAML ParentChildRelation ABI mismatch");
static_assert(sizeof(VisualElement) == 64, "XAML VisualElement ABI mismatch");
static_assert(sizeof(PropertyChainSource) == 64, "XAML PropertyChainSource ABI mismatch");
static_assert(sizeof(PropertyChainValue) == 80, "XAML PropertyChainValue ABI mismatch");
#endif

static const IID IID_IVisualTreeServiceCallback = {0xaa7a8931,0x80e4,0x4fec,{0x8f,0x3b,0x55,0x3f,0x87,0xb4,0x96,0x6e}};
static const IID IID_IVisualTreeServiceCallback2 = {0xbad9eb88,0xae77,0x4397,{0xb9,0x48,0x5f,0xa2,0xdb,0x0a,0x19,0xea}};
static const IID IID_IVisualTreeService = {0xa593b11a,0xd17f,0x48bb,{0x8f,0x66,0x83,0x91,0x07,0x31,0xc8,0xa5}};
static const IID IID_IXamlDiagnostics = {0x18c9e2b6,0x3f43,0x4116,{0x9f,0x2b,0xff,0x93,0x5d,0x77,0x70,0xd2}};

struct IVisualTreeServiceCallback : IUnknown {
    virtual HRESULT STDMETHODCALLTYPE OnVisualTreeChange(ParentChildRelation relation, VisualElement element, VisualMutationType mutationType) = 0;
};
struct IVisualTreeServiceCallback2 : IVisualTreeServiceCallback {
    virtual HRESULT STDMETHODCALLTYPE OnElementStateChanged(InstanceHandle element, VisualElementState elementState, LPCWSTR context) = 0;
};
struct IVisualTreeService : IUnknown {
    virtual HRESULT STDMETHODCALLTYPE AdviseVisualTreeChange(IVisualTreeServiceCallback* callback) = 0;
    virtual HRESULT STDMETHODCALLTYPE UnadviseVisualTreeChange(IVisualTreeServiceCallback* callback) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetEnums(UINT* count, EnumType** enums) = 0;
    virtual HRESULT STDMETHODCALLTYPE CreateInstance(BSTR typeName, BSTR value, InstanceHandle* instanceHandle) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetPropertyValuesChain(InstanceHandle instanceHandle, UINT* sourceCount, PropertyChainSource** propertySources, UINT* propertyCount, PropertyChainValue** propertyValues) = 0;
    virtual HRESULT STDMETHODCALLTYPE SetProperty(InstanceHandle instanceHandle, InstanceHandle value, UINT propertyIndex) = 0;
    virtual HRESULT STDMETHODCALLTYPE ClearProperty(InstanceHandle instanceHandle, UINT propertyIndex) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetCollectionCount(InstanceHandle instanceHandle, UINT* collectionSize) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetCollectionElements(InstanceHandle instanceHandle, UINT startIndex, UINT* elementCount, CollectionElementValue** elementValues) = 0;
    virtual HRESULT STDMETHODCALLTYPE AddChild(InstanceHandle parent, InstanceHandle child, UINT index) = 0;
    virtual HRESULT STDMETHODCALLTYPE RemoveChild(InstanceHandle parent, UINT index) = 0;
    virtual HRESULT STDMETHODCALLTYPE ClearChildren(InstanceHandle parent) = 0;
};
struct IXamlDiagnostics : IUnknown {
    virtual HRESULT STDMETHODCALLTYPE GetDispatcher(IInspectable** dispatcher) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetUiLayer(IInspectable** layer) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetApplication(IInspectable** application) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetIInspectableFromHandle(InstanceHandle instanceHandle, IInspectable** instance) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetHandleFromIInspectable(IInspectable* instance, InstanceHandle* handle) = 0;
    virtual HRESULT STDMETHODCALLTYPE HitTest(RECT rect, UINT* count, InstanceHandle** instanceHandles) = 0;
    virtual HRESULT STDMETHODCALLTYPE RegisterInstance(IInspectable* instance, InstanceHandle* instanceHandle) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetInitializationData(BSTR* initializationData) = 0;
};

typedef HRESULT (WINAPI* InitializeXamlDiagnosticsExFn)(LPCWSTR endpointName, DWORD pid,
    LPCWSTR xamlDiagnosticsDll, LPCWSTR tapDllName, CLSID tapClsid, LPCWSTR initializationData);

} // namespace XamlAbi
