// A stand-in for a DirectX 11 game, for the end-to-end test of the D3D11 recorder (proxy/test_d3d11rec_app.ps1): it imports
// d3d11.dll the way a game does, so the loader takes the d3d11.dll next to it, which is the recorder once the app installs it.
// It creates a few shaders (a vertex, a pixel and a compute shader, a hull/domain pair it binds) and exits. Never shipped.
#define NOMINMAX
#include <windows.h>
#define D3D11_NO_HELPERS
#include <d3d11.h>
#include <d3dcompiler.h>
#include <cstdio>
#include <string>

#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "d3dcompiler.lib")

static ID3DBlob* compile(const char* src, const char* target) {
    ID3DBlob *b = nullptr, *e = nullptr;
    if (FAILED(D3DCompile(src, strlen(src), "fake", nullptr, nullptr, "main", target, 0, 0, &b, &e))) {
        printf("compile failed: %s\n", e ? (const char*)e->GetBufferPointer() : "?");
        return nullptr;
    }
    return b;
}

int main(int argc, char** argv) {
    // a seed so each run's shaders are new to the driver's cache (argv[1], default 0)
    const int seed = argc > 1 ? atoi(argv[1]) : 0;
    ID3D11Device* dev = nullptr;
    ID3D11DeviceContext* ctx = nullptr;
    D3D_FEATURE_LEVEL fl = D3D_FEATURE_LEVEL_11_0, got{};
    HRESULT hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0, &fl, 1, D3D11_SDK_VERSION, &dev, &got, &ctx);
    if (FAILED(hr)) return printf("D3D11CreateDevice 0x%08x\n", (unsigned)hr), 1;
    const std::string k = std::to_string(seed);
    const std::string vs = "float4 main(float4 p : POSITION) : SV_Position { return p * " + k + ".5; }";
    const std::string ps = "float4 main(float4 p : SV_Position) : SV_Target { return float4(0.25, 0.5, " + k + ".0 / 255.0, 1); }";
    const std::string cs = "RWStructuredBuffer<uint> o : register(u0); [numthreads(1, 1, 1)] void main(uint3 t : SV_DispatchThreadID) { o[t.x] = t.x + " + k + "; }";
    const std::string hs =
        "struct V { float4 p : POSITION; }; struct PC { float e[3] : SV_TessFactor; float i : SV_InsideTessFactor; };"
        "PC pcf(InputPatch<V, 3> ip) { PC o; o.e[0] = o.e[1] = o.e[2] = " + k + ".5; o.i = 1; return o; }"
        "[domain(\"tri\")][partitioning(\"integer\")][outputtopology(\"triangle_cw\")][outputcontrolpoints(3)][patchconstantfunc(\"pcf\")]"
        "V main(InputPatch<V, 3> ip, uint i : SV_OutputControlPointID) { return ip[i]; }";
    const std::string ds =
        "struct V { float4 p : POSITION; }; struct PC { float e[3] : SV_TessFactor; float i : SV_InsideTessFactor; };"
        "[domain(\"tri\")] float4 main(PC pc, float3 uvw : SV_DomainLocation, const OutputPatch<V, 3> t) : SV_Position"
        "{ return t[0].p * uvw.x + t[1].p * uvw.y + t[2].p * uvw.z + " + k + ".25; }";
    ID3DBlob *bvs = compile(vs.c_str(), "vs_5_0"), *bps = compile(ps.c_str(), "ps_5_0"), *bcs = compile(cs.c_str(), "cs_5_0"),
             *bhs = compile(hs.c_str(), "hs_5_0"), *bds = compile(ds.c_str(), "ds_5_0");
    if (!bvs || !bps || !bcs || !bhs || !bds) return 1;
    ID3D11VertexShader* v = nullptr;
    ID3D11PixelShader* p = nullptr;
    ID3D11ComputeShader* c = nullptr;
    ID3D11HullShader* h = nullptr;
    ID3D11DomainShader* d = nullptr;
    if (FAILED(dev->CreateVertexShader(bvs->GetBufferPointer(), bvs->GetBufferSize(), nullptr, &v)) ||
        FAILED(dev->CreatePixelShader(bps->GetBufferPointer(), bps->GetBufferSize(), nullptr, &p)) ||
        FAILED(dev->CreateComputeShader(bcs->GetBufferPointer(), bcs->GetBufferSize(), nullptr, &c)) ||
        FAILED(dev->CreateHullShader(bhs->GetBufferPointer(), bhs->GetBufferSize(), nullptr, &h)) ||
        FAILED(dev->CreateDomainShader(bds->GetBufferPointer(), bds->GetBufferSize(), nullptr, &d)))
        return printf("a shader create failed\n"), 1;
    ctx->HSSetShader(h, nullptr, 0);
    ctx->DSSetShader(d, nullptr, 0);
    printf("fakegame11: 5 shaders created (seed %d)\n", seed);
    ctx->Release(), dev->Release();
    return 0;
}
