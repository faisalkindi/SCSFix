; Raw jmp forwarders to the system dxgi.dll (the diagnostic pure forwarder, pure_dxgi.cpp).
STUB MACRO name
    EXTERN real_&name:QWORD
    stub_&name PROC
        jmp QWORD PTR [real_&name]
    stub_&name ENDP
ENDM

.code
STUB ApplyCompatResolutionQuirking
STUB CompatString
STUB CompatValue
STUB CreateDXGIFactory
STUB CreateDXGIFactory1
STUB CreateDXGIFactory2
STUB DXGID3D10CreateDevice
STUB DXGID3D10CreateLayeredDevice
STUB DXGID3D10GetLayeredDeviceSize
STUB DXGID3D10RegisterLayers
STUB DXGIDeclareAdapterRemovalSupport
STUB DXGIDisableVBlankVirtualization
STUB DXGIDumpJournal
STUB DXGIGetDebugInterface1
STUB DXGIReportAdapterConfiguration
STUB PIXBeginCapture
STUB PIXEndCapture
STUB PIXGetCaptureState
STUB SetAppCompatStringPointer
STUB UpdateHMDEmulationStatus
END
