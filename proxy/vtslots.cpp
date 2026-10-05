// Compile-time check of the D3D12, D3D11 and DXGI vtable slots proxy.cpp patches: the C interface's Vtbl structs
// list the methods in COM ABI order, so a slot is the method's offset in its Vtbl. Nothing here is linked into anything.
#define CINTERFACE
#include <windows.h>
#include <d3d12.h>
#define D3D11_NO_HELPERS  // the CD3D11_* helpers call methods the C interface doesn't have
#include <d3d11.h>
#include <dxgi1_2.h>
#include <cstddef>

#define SLOT(I, M, N) static_assert(offsetof(I##Vtbl, M) == (N) * sizeof(void*), #I "::" #M " is not at vtable slot " #N);
SLOT(ID3D12Device, CreateGraphicsPipelineState, 10)
SLOT(ID3D12Device, CreateComputePipelineState, 11)
SLOT(ID3D12Device, CreateRootSignature, 16)
SLOT(ID3D12Device1, CreatePipelineLibrary, 44)
SLOT(ID3D12Device2, CreatePipelineState, 47)
SLOT(ID3D12Device5, CreateStateObject, 62)
SLOT(ID3D12Device7, AddToStateObject, 66)
SLOT(ID3D12PipelineLibrary, LoadGraphicsPipeline, 9)
SLOT(ID3D12PipelineLibrary, LoadComputePipeline, 10)
SLOT(ID3D12PipelineLibrary1, LoadPipeline, 13)
SLOT(ID3D12DeviceFactory, CreateDevice, 9)
SLOT(ID3D12SDKConfiguration1, CreateDeviceFactory, 4)
SLOT(ID3D11Device, CreateVertexShader, 12)
SLOT(ID3D11Device, CreateGeometryShader, 13)
SLOT(ID3D11Device, CreatePixelShader, 15)
SLOT(ID3D11Device, CreateHullShader, 16)
SLOT(ID3D11Device, CreateDomainShader, 17)
SLOT(ID3D11Device, CreateComputeShader, 18)
SLOT(ID3D11DeviceContext, HSSetShader, 60)
SLOT(ID3D11DeviceContext, DSSetShader, 64)
SLOT(IDXGISwapChain, Present, 8)
SLOT(IDXGISwapChain1, Present1, 22)
SLOT(IDXGIFactory, CreateSwapChain, 10)
SLOT(IDXGIFactory2, CreateSwapChainForHwnd, 15)
SLOT(IDXGIFactory2, CreateSwapChainForCoreWindow, 16)
SLOT(IDXGIFactory2, CreateSwapChainForComposition, 24)
