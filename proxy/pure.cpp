// A d3d12.dll that only forwards, for diagnosis (a game whose protection objects to the recorder): the system d3d12.dll's exports, nothing
// else: no hooks, no files, no threads, no log. Test only, never published. Built as d3d12_pure.dll; a game folder's copy is named d3d12.dll.
#include <windows.h>

#define NAMES(X)                                                                                                 \
    X(D3D12CoreCreateLayeredDevice) X(D3D12CoreGetLayeredDeviceSize) X(D3D12CoreRegisterLayers)                  \
    X(D3D12CreateDevice) X(D3D12CreateRootSignatureDeserializer) X(D3D12CreateVersionedRootSignatureDeserializer) \
    X(D3D12DeviceRemovedExtendedData) X(D3D12EnableExperimentalFeatures) X(D3D12GetDebugInterface)               \
    X(D3D12GetInterface) X(D3D12PIXEventsReplaceBlock) X(D3D12PIXGetThreadInfo) X(D3D12PIXNotifyWakeFromFenceSignal) \
    X(D3D12PIXReportCounter) X(D3D12SerializeRootSignature) X(D3D12SerializeVersionedRootSignature)              \
    X(GetBehaviorValue) X(SetAppCompatStringPointer)

extern "C" {
#define DECL(n) void* real_##n;
NAMES(DECL)
#undef DECL
void* real_Ordinal99;
}

BOOL WINAPI DllMain(HINSTANCE self, DWORD reason, LPVOID) {
    if (reason != DLL_PROCESS_ATTACH) return TRUE;
    DisableThreadLibraryCalls(self);
    wchar_t p[MAX_PATH];
    UINT n = GetSystemDirectoryW(p, MAX_PATH);
    if (!n || n + 12 >= MAX_PATH) return FALSE;
    lstrcpyW(p + n, L"\\d3d12.dll");
    HMODULE real = LoadLibraryW(p);
    if (!real) return FALSE;
#define RES(n) real_##n = (void*)GetProcAddress(real, #n);
    NAMES(RES)
#undef RES
    real_Ordinal99 = (void*)GetProcAddress(real, MAKEINTRESOURCEA(99));
    return TRUE;
}
