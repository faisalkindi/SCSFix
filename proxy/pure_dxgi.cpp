// A dxgi.dll that only forwards, for diagnosis (does a game's protection object to any DLL of ours in its folder, or to d3d12.dll in
// particular?): the system dxgi.dll's exports, nothing else. Test only, never published. Built as dxgi_pure.dll; a game folder's copy is
// named dxgi.dll.
#include <windows.h>

#define NAMES(X)                                                                                                    \
    X(ApplyCompatResolutionQuirking) X(CompatString) X(CompatValue) X(CreateDXGIFactory) X(CreateDXGIFactory1)       \
    X(CreateDXGIFactory2) X(DXGID3D10CreateDevice) X(DXGID3D10CreateLayeredDevice) X(DXGID3D10GetLayeredDeviceSize)  \
    X(DXGID3D10RegisterLayers) X(DXGIDeclareAdapterRemovalSupport) X(DXGIDisableVBlankVirtualization)                \
    X(DXGIDumpJournal) X(DXGIGetDebugInterface1) X(DXGIReportAdapterConfiguration) X(PIXBeginCapture) X(PIXEndCapture) \
    X(PIXGetCaptureState) X(SetAppCompatStringPointer) X(UpdateHMDEmulationStatus)

extern "C" {
#define DECL(n) void* real_##n;
NAMES(DECL)
#undef DECL
}

BOOL WINAPI DllMain(HINSTANCE self, DWORD reason, LPVOID) {
    if (reason != DLL_PROCESS_ATTACH) return TRUE;
    DisableThreadLibraryCalls(self);
    wchar_t p[MAX_PATH];
    UINT n = GetSystemDirectoryW(p, MAX_PATH);
    if (!n || n + 12 >= MAX_PATH) return FALSE;
    lstrcpyW(p + n, L"\\dxgi.dll");
    HMODULE real = LoadLibraryW(p);
    if (!real) return FALSE;
#define RES(n) real_##n = (void*)GetProcAddress(real, #n);
    NAMES(RES)
#undef RES
    return TRUE;
}
