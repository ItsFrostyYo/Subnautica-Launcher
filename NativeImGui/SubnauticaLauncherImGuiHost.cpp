#include "imgui.h"
#include "imgui_internal.h"
#include "backends/imgui_impl_dx11.h"
#include "backends/imgui_impl_win32.h"
#include <d3d11.h>
#include <dwmapi.h>
#include <shellapi.h>
#include <shlobj.h>
#include <shobjidl.h>
#include <wincrypt.h>
#include <wincodec.h>
#include <windows.h>
#include <windowsx.h>
#include <algorithm>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <string>
#include <vector>
#include <cstring>
#include <cstdlib>
#include <iterator>
#include <sstream>

#pragma comment(lib, "d3d11.lib")

extern IMGUI_IMPL_API LRESULT ImGui_ImplWin32_WndProcHandler(HWND hWnd, UINT message, WPARAM wParam, LPARAM lParam);

static ID3D11Device* g_device = nullptr;
static ID3D11DeviceContext* g_context = nullptr;
static IDXGISwapChain* g_swapChain = nullptr;
static ID3D11RenderTargetView* g_target = nullptr;
static ID3D11ShaderResourceView* g_background = nullptr;
static ID3D11ShaderResourceView* g_icon = nullptr;
static std::wstring g_iconDirectory;
static UINT g_resizeWidth = 0;
static UINT g_resizeHeight = 0;
static bool g_done = false;
static LONG g_minWidth = 380;
static LONG g_minHeight = 380;
static bool g_hasBackButton = false;
static bool g_showDeleteConfirmation = false;

struct ManagedSettingsVersion
{
    std::string path;
    std::string displayName;
    bool hasSettings = false;
};

struct SavedSettingEntry
{
    std::string category;
    std::string name;
    std::string value;
};

struct ModWindowVersion
{
    std::string path;
    std::string displayName;
};

struct ModWindowCard
{
    std::string id;
    std::string name;
    std::string author;
    std::string version;
    std::string description;
    std::string action;
    bool installed = false;
    bool canAction = true;
    std::string unavailableReason;
};

// Title-bar controls: adjust these to change spacing without breaking hit testing.
static constexpr float kTitleButtonSize = 19.0f;
static constexpr float kTitleBarHeight = 30.0f;
static constexpr float kTitleButtonTop = 8.0f;
static constexpr float kTitleRightPadding = 17.0f;
static constexpr float kTitleButtonGap = 5.0f;

static std::wstring ToWide(const std::string& value);

static void CleanupTarget()
{
    if (g_target) { g_target->Release(); g_target = nullptr; }
}

static void CreateTarget()
{
    ID3D11Texture2D* backBuffer = nullptr;
    g_swapChain->GetBuffer(0, IID_PPV_ARGS(&backBuffer));
    g_device->CreateRenderTargetView(backBuffer, nullptr, &g_target);
    backBuffer->Release();
}

static bool CreateDevice(HWND window)
{
    DXGI_SWAP_CHAIN_DESC description{};
    description.BufferCount = 2;
    description.BufferDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    description.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    description.OutputWindow = window;
    description.SampleDesc.Count = 1;
    description.Windowed = TRUE;
    description.SwapEffect = DXGI_SWAP_EFFECT_DISCARD;
    D3D_FEATURE_LEVEL levels[] = { D3D_FEATURE_LEVEL_11_0, D3D_FEATURE_LEVEL_10_0 };
    D3D_FEATURE_LEVEL level{};
    return SUCCEEDED(D3D11CreateDeviceAndSwapChain(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0, levels, ARRAYSIZE(levels), D3D11_SDK_VERSION, &description, &g_swapChain, &g_device, &level, &g_context)) && (CreateTarget(), true);
}

static void CleanupDevice()
{
    if (g_background) { g_background->Release(); g_background = nullptr; }
    if (g_icon) { g_icon->Release(); g_icon = nullptr; }
    CleanupTarget();
    if (g_swapChain) { g_swapChain->Release(); g_swapChain = nullptr; }
    if (g_context) { g_context->Release(); g_context = nullptr; }
    if (g_device) { g_device->Release(); g_device = nullptr; }
}

// Match the original launcher child windows, which use a WPF BlurEffect with Radius=8.
static void BlurBackground(std::vector<BYTE>& pixels, UINT width, UINT height, int radius)
{
    if (radius <= 0 || width == 0 || height == 0) return;

    std::vector<BYTE> temporary(pixels.size());
    const int diameter = radius * 2 + 1;
    for (UINT y = 0; y < height; ++y)
    {
        for (UINT x = 0; x < width; ++x)
        {
            int totals[4]{};
            for (int offset = -radius; offset <= radius; ++offset)
            {
                UINT sampleX = static_cast<UINT>(std::clamp(static_cast<int>(x) + offset, 0, static_cast<int>(width) - 1));
                size_t index = (static_cast<size_t>(y) * width + sampleX) * 4;
                for (int channel = 0; channel < 4; ++channel) totals[channel] += pixels[index + channel];
            }
            size_t target = (static_cast<size_t>(y) * width + x) * 4;
            for (int channel = 0; channel < 4; ++channel) temporary[target + channel] = static_cast<BYTE>(totals[channel] / diameter);
        }
    }
    for (UINT y = 0; y < height; ++y)
    {
        for (UINT x = 0; x < width; ++x)
        {
            int totals[4]{};
            for (int offset = -radius; offset <= radius; ++offset)
            {
                UINT sampleY = static_cast<UINT>(std::clamp(static_cast<int>(y) + offset, 0, static_cast<int>(height) - 1));
                size_t index = (static_cast<size_t>(sampleY) * width + x) * 4;
                for (int channel = 0; channel < 4; ++channel) totals[channel] += temporary[index + channel];
            }
            size_t target = (static_cast<size_t>(y) * width + x) * 4;
            for (int channel = 0; channel < 4; ++channel) pixels[target + channel] = static_cast<BYTE>(totals[channel] / diameter);
        }
    }
}

static bool LoadTexture(const std::wstring& path, ID3D11ShaderResourceView** destination, bool blur)
{
    if (path.empty() || GetFileAttributesW(path.c_str()) == INVALID_FILE_ATTRIBUTES) return false;
    IWICImagingFactory* factory = nullptr; IWICBitmapDecoder* decoder = nullptr; IWICBitmapFrameDecode* frame = nullptr; IWICFormatConverter* converter = nullptr;
    bool success = false; UINT width = 0, height = 0; ID3D11Texture2D* texture = nullptr;
    do {
        if (FAILED(CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&factory)))) break;
        if (FAILED(factory->CreateDecoderFromFilename(path.c_str(), nullptr, GENERIC_READ, WICDecodeMetadataCacheOnLoad, &decoder))) break;
        if (FAILED(decoder->GetFrame(0, &frame)) || FAILED(factory->CreateFormatConverter(&converter))) break;
        if (FAILED(converter->Initialize(frame, GUID_WICPixelFormat32bppRGBA, WICBitmapDitherTypeNone, nullptr, 0.0, WICBitmapPaletteTypeCustom))) break;
        converter->GetSize(&width, &height); std::vector<BYTE> pixels(width * height * 4);
        if (FAILED(converter->CopyPixels(nullptr, width * 4, static_cast<UINT>(pixels.size()), pixels.data()))) break;
        if (blur) BlurBackground(pixels, width, height, 8);
        D3D11_TEXTURE2D_DESC desc{}; desc.Width = width; desc.Height = height; desc.MipLevels = 1; desc.ArraySize = 1; desc.Format = DXGI_FORMAT_R8G8B8A8_UNORM; desc.SampleDesc.Count = 1; desc.Usage = D3D11_USAGE_DEFAULT; desc.BindFlags = D3D11_BIND_SHADER_RESOURCE;
        D3D11_SUBRESOURCE_DATA data{}; data.pSysMem = pixels.data(); data.SysMemPitch = width * 4;
        if (SUCCEEDED(g_device->CreateTexture2D(&desc, &data, &texture)))
        {
            ID3D11ShaderResourceView* view = nullptr;
            if (SUCCEEDED(g_device->CreateShaderResourceView(texture, nullptr, &view)))
            {
                if (*destination) (*destination)->Release();
                *destination = view;
                success = true;
            }
        }
    } while (false);
    if (texture) texture->Release();
    if (converter) converter->Release(); if (frame) frame->Release(); if (decoder) decoder->Release(); if (factory) factory->Release();
    return success;
}

static bool LoadBackgroundTexture(const std::wstring& path) { return LoadTexture(path, &g_background, true); }

static void LoadSelectedIcon(const char* iconName)
{
    if (g_iconDirectory.empty() || !iconName || !*iconName) return;
    LoadTexture(g_iconDirectory + ToWide(iconName), &g_icon, false);
}

static const char* GetIconDisplayName(const char* iconName)
{
    if (std::strcmp(iconName, "SNLIcon.png") == 0) return "Subnautica Mobile Icon";
    if (std::strcmp(iconName, "SNLIconBZ.png") == 0) return "Below Zero Icon";
    if (std::strcmp(iconName, "StalkerTooth.png") == 0) return "Stalker Tooth";
    if (std::strcmp(iconName, "HatchingEnzymes.png") == 0) return "Hatching Enzymes";
    if (std::strcmp(iconName, "IonCube.png") == 0) return "Ion Cube";
    if (std::strcmp(iconName, "Gold.png") == 0) return "Gold";
    if (std::strcmp(iconName, "Titanium.png") == 0) return "Titanium";
    if (std::strcmp(iconName, "Kyanite.png") == 0) return "Kyanite";
    return "Subnautica Mobile Icon";
}

static std::string FindSteamLaunchOptions()
{
    wchar_t programFiles[MAX_PATH]{};
    DWORD length = GetEnvironmentVariableW(L"ProgramFiles(x86)", programFiles, ARRAYSIZE(programFiles));
    if (length == 0 || length >= ARRAYSIZE(programFiles)) return {};
    std::filesystem::path userdata = std::filesystem::path(programFiles) / L"Steam" / L"userdata";
    std::error_code error;
    if (!std::filesystem::is_directory(userdata, error)) return {};

    for (const auto& user : std::filesystem::directory_iterator(userdata, error))
    {
        std::filesystem::path config = user.path() / L"config" / L"localconfig.vdf";
        std::ifstream stream(config, std::ios::binary);
        if (!stream) continue;
        std::string text((std::istreambuf_iterator<char>(stream)), std::istreambuf_iterator<char>());
        size_t app = text.find("\"264710\"");
        if (app == std::string::npos) continue;
        size_t optionKey = text.find("\"LaunchOptions\"", app);
        if (optionKey == std::string::npos) continue;
        size_t valueStart = text.find('"', optionKey + 15);
        if (valueStart == std::string::npos) continue;
        size_t valueEnd = text.find('"', valueStart + 1);
        if (valueEnd == std::string::npos) continue;
        return text.substr(valueStart + 1, valueEnd - valueStart - 1);
    }
    return {};
}

static LRESULT WINAPI WindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
{
    if (ImGui_ImplWin32_WndProcHandler(window, message, wParam, lParam))
        return true;

    switch (message)
    {
    case WM_NCHITTEST:
    {
        LRESULT hit = DefWindowProcW(window, message, wParam, lParam);
        if (hit != HTCLIENT) return hit;
        POINT point{ GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam) };
        ScreenToClient(window, &point);
        RECT rect{}; GetClientRect(window, &rect);
        const int border = 7;
        if (point.x < border && point.y < border) return HTTOPLEFT;
        if (point.x >= rect.right - border && point.y < border) return HTTOPRIGHT;
        if (point.x < border && point.y >= rect.bottom - border) return HTBOTTOMLEFT;
        if (point.x >= rect.right - border && point.y >= rect.bottom - border) return HTBOTTOMRIGHT;
        if (point.x < border) return HTLEFT;
        if (point.x >= rect.right - border) return HTRIGHT;
        if (point.y < border) return HTTOP;
        if (point.y >= rect.bottom - border) return HTBOTTOM;
        // The custom ImGui title strip owns its right-side controls; its remaining area moves the window.
        const int titleButtonsWidth = static_cast<int>(kTitleRightPadding + kTitleButtonSize
            + (g_hasBackButton ? kTitleButtonGap + kTitleButtonSize : 0.0f));
        if (point.y < static_cast<int>(kTitleBarHeight) && point.x < rect.right - titleButtonsWidth) return HTCAPTION;
        return HTCLIENT;
    }
    case WM_SIZE:
        if (wParam != SIZE_MINIMIZED) { g_resizeWidth = LOWORD(lParam); g_resizeHeight = HIWORD(lParam); }
        return 0;
    case WM_GETMINMAXINFO:
        reinterpret_cast<MINMAXINFO*>(lParam)->ptMinTrackSize = POINT{ g_minWidth, g_minHeight };
        return 0;
    case WM_CLOSE:
        g_done = true;
        return 0;
    case WM_DESTROY:
        PostQuitMessage(0);
        return 0;
    }
    return DefWindowProcW(window, message, wParam, lParam);
}

static std::wstring ToWide(const std::string& value)
{
    if (value.empty()) return {};
    int count = MultiByteToWideChar(CP_UTF8, 0, value.c_str(), -1, nullptr, 0);
    std::wstring result(count, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, value.c_str(), -1, result.data(), count);
    result.resize(count - 1);
    return result;
}

static std::string ToUtf8(const std::wstring& value)
{
    if (value.empty()) return {};
    int count = WideCharToMultiByte(CP_UTF8, 0, value.c_str(), -1, nullptr, 0, nullptr, nullptr);
    std::string result(count, '\0');
    WideCharToMultiByte(CP_UTF8, 0, value.c_str(), -1, result.data(), count, nullptr, nullptr);
    result.resize(count - 1);
    return result;
}

static std::string Encode(const std::string& value)
{
    if (value.empty()) return "";
    DWORD length = 0;
    CryptBinaryToStringA(reinterpret_cast<const BYTE*>(value.data()), static_cast<DWORD>(value.size()), CRYPT_STRING_BASE64 | CRYPT_STRING_NOCRLF, nullptr, &length);
    std::vector<char> buffer(length + 1, '\0');
    DWORD capacity = static_cast<DWORD>(buffer.size());
    if (!CryptBinaryToStringA(reinterpret_cast<const BYTE*>(value.data()), static_cast<DWORD>(value.size()), CRYPT_STRING_BASE64 | CRYPT_STRING_NOCRLF, buffer.data(), &capacity))
        return "";
    return std::string(buffer.data());
}

static std::string Decode(const std::string& value)
{
    if (value.empty()) return "";
    DWORD length = 0;
    if (!CryptStringToBinaryA(value.c_str(), static_cast<DWORD>(value.size()), CRYPT_STRING_BASE64, nullptr, &length, nullptr, nullptr))
        return "";
    std::vector<BYTE> buffer(length);
    if (!CryptStringToBinaryA(value.c_str(), static_cast<DWORD>(value.size()), CRYPT_STRING_BASE64, buffer.data(), &length, nullptr, nullptr))
        return "";
    return std::string(reinterpret_cast<const char*>(buffer.data()), length);
}

static std::string GetArgument(const std::vector<std::string>& args, const std::string& name)
{
    for (size_t i = 0; i + 1 < args.size(); ++i)
        if (args[i] == name) return args[i + 1];
    return {};
}

static void WriteResult(const std::string& result)
{
    HANDLE output = GetStdHandle(STD_OUTPUT_HANDLE);
    if (output == nullptr || output == INVALID_HANDLE_VALUE || result.empty())
        return;

    std::string line = result + "\n";
    DWORD written = 0;
    WriteFile(output, line.data(), static_cast<DWORD>(line.size()), &written, nullptr);
    FlushFileBuffers(output);
}

static std::vector<std::string> PollProtocolInput(std::string& pending)
{
    std::vector<std::string> lines;
    HANDLE input = GetStdHandle(STD_INPUT_HANDLE);
    if (input == nullptr || input == INVALID_HANDLE_VALUE) return lines;
    DWORD available = 0;
    while (PeekNamedPipe(input, nullptr, 0, nullptr, &available, nullptr) && available > 0)
    {
        char buffer[2048];
        DWORD read = 0;
        if (!ReadFile(input, buffer, (std::min)(available, static_cast<DWORD>(sizeof(buffer))), &read, nullptr) || read == 0) break;
        pending.append(buffer, read);
        while (true)
        {
            size_t newline = pending.find('\n');
            if (newline == std::string::npos) break;
            std::string line = pending.substr(0, newline);
            if (!line.empty() && line.back() == '\r') line.pop_back();
            pending.erase(0, newline + 1);
            if (!line.empty()) lines.push_back(std::move(line));
        }
        available = 0;
    }
    return lines;
}

// Mirrors Dear ImGui's CloseButton behavior, but renders a left arrow for navigation.
static bool LauncherButton(const char* label, const ImVec2& size = ImVec2(0, 0))
{
    bool pressed = ImGui::Button(label, size);
    if (ImGui::IsItemHovered()) ImGui::SetMouseCursor(ImGuiMouseCursor_Hand);
    return pressed;
}

static bool TitleIconButton(const char* id, const ImVec2& position, bool isBack)
{
    const float size = kTitleButtonSize;
    ImGui::SetCursorScreenPos(position);
    ImGui::InvisibleButton(id, ImVec2(size, size));
    bool hovered = ImGui::IsItemHovered();
    bool pressed = ImGui::IsItemClicked();
    ImDrawList* drawList = ImGui::GetWindowDrawList();

    // Keep both symbols fully inside the clickable rectangle, away from the rounded window corners.
    if (hovered)
    {
        ImGui::SetMouseCursor(ImGuiMouseCursor_Hand);
        drawList->AddRectFilled(position, ImVec2(position.x + size, position.y + size),
            isBack ? IM_COL32(88, 128, 174, 145) : IM_COL32(180, 48, 56, 110), 4.0f);
    }

    ImVec2 center(position.x + size * 0.5f, position.y + size * 0.5f);
    if (isBack)
    {
        // Real left-facing arrow with a shaft and two clean arrowhead strokes.
        const ImU32 arrowColor = IM_COL32(240, 246, 255, 255);
        drawList->AddLine(ImVec2(center.x - 5.0f, center.y),
                          ImVec2(center.x + 5.0f, center.y), arrowColor, 1.9f);
        drawList->AddLine(ImVec2(center.x - 5.0f, center.y),
                          ImVec2(center.x - 0.5f, center.y - 4.5f), arrowColor, 1.9f);
        drawList->AddLine(ImVec2(center.x - 5.0f, center.y),
                          ImVec2(center.x - 0.5f, center.y + 4.5f), arrowColor, 1.9f);
    }
    else
    {
        // Clean red close icon, drawn directly instead of relying on a font glyph.
        const ImU32 closeColor = hovered ? IM_COL32(255, 110, 115, 255)
                                         : IM_COL32(245, 75, 85, 255);
        drawList->AddLine(ImVec2(center.x - 4.3f, center.y - 4.3f),
                          ImVec2(center.x + 4.3f, center.y + 4.3f), closeColor, 2.1f);
        drawList->AddLine(ImVec2(center.x + 4.3f, center.y - 4.3f),
                          ImVec2(center.x - 4.3f, center.y + 4.3f), closeColor, 2.1f);
    }
    return pressed;
}

static void ResizeForMode(HWND window, const std::string& mode)
{
    const int width = mode == "choice" ? 400 : mode == "edit" ? 600 : mode == "install" ? 400 : mode == "login" ? 480 : mode == "progress" ? 760 : mode == "settings" ? 480 : mode == "mods" ? 760 : 380;
    const int height = mode == "choice" ? 180 : mode == "edit" ? 420 : mode == "install" ? 400 : mode == "login" ? 490 : mode == "progress" ? 560 : mode == "settings" ? 390 : mode == "mods" ? 590 : 340;
    g_minWidth = width;
    g_minHeight = height;
    g_hasBackButton = mode == "existing" || mode == "install" || mode == "login";
    SetWindowPos(window, nullptr, 0, 0, width, height, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
}

static void ResizeSettingsPage(HWND window, int page)
{
    const int width = page == 2 ? 760 : 480;
    const int height = page == 2 ? 560 : page == 1 ? 310 : 390;
    RECT bounds{};
    GetWindowRect(window, &bounds);
    const int centerX = bounds.left + (bounds.right - bounds.left) / 2;
    const int centerY = bounds.top + (bounds.bottom - bounds.top) / 2;
    g_minWidth = width;
    g_minHeight = height;
    SetWindowPos(window, nullptr, centerX - width / 2, centerY - height / 2, width, height, SWP_NOZORDER | SWP_NOACTIVATE);
}

static bool BrowseForSubnauticaExecutable(char* target, size_t targetSize)
{
    IFileOpenDialog* dialog = nullptr;
    if (FAILED(CoCreateInstance(CLSID_FileOpenDialog, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&dialog))))
        return false;

    COMDLG_FILTERSPEC filters[] = { { L"Subnautica executable", L"Subnautica.exe" } };
    dialog->SetFileTypes(ARRAYSIZE(filters), filters);
    dialog->SetFileName(L"Subnautica.exe");
    DWORD options{};
    dialog->GetOptions(&options);
    dialog->SetOptions(options | FOS_FORCEFILESYSTEM | FOS_FILEMUSTEXIST | FOS_PATHMUSTEXIST);
    HRESULT result = dialog->Show(nullptr);
    if (FAILED(result)) { dialog->Release(); return false; }

    IShellItem* item = nullptr;
    result = dialog->GetResult(&item);
    if (FAILED(result)) { dialog->Release(); return false; }
    PWSTR path = nullptr;
    result = item->GetDisplayName(SIGDN_FILESYSPATH, &path);
    item->Release();
    dialog->Release();
    if (FAILED(result) || !path) return false;
    std::string utf8 = ToUtf8(path);
    CoTaskMemFree(path);
    strncpy_s(target, targetSize, utf8.c_str(), _TRUNCATE);
    return true;
}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int)
{
    int argc = 0;
    LPWSTR* rawArgs = CommandLineToArgvW(GetCommandLineW(), &argc);
    std::vector<std::string> args;
    for (int i = 1; i < argc; ++i) args.push_back(ToUtf8(rawArgs[i]));
    LocalFree(rawArgs);
    std::string mode = GetArgument(args, "--mode");
    std::string ownerText = GetArgument(args, "--owner");
    HWND owner = ownerText.empty() ? nullptr : reinterpret_cast<HWND>(_strtoi64(ownerText.c_str(), nullptr, 10));
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED | COINIT_DISABLE_OLE1DDE);

    WNDCLASSEXW windowClass{ sizeof(windowClass), CS_CLASSDC, WindowProc, 0, 0, instance, nullptr, nullptr, nullptr, nullptr, L"SubnauticaLauncherNativeImGui", nullptr };
    RegisterClassExW(&windowClass);
    int width = mode == "choice" ? 400 : mode == "edit" ? 600 : mode == "install" ? 400 : mode == "login" ? 480 : mode == "progress" ? 760 : mode == "settings" ? 480 : mode == "mods" ? 760 : 380;
    int height = mode == "choice" ? 180 : mode == "edit" ? 420 : mode == "install" ? 400 : mode == "login" ? 490 : mode == "progress" ? 560 : mode == "settings" ? 390 : mode == "mods" ? 590 : 340;
    g_minWidth = width;
    g_minHeight = height;
    g_hasBackButton = mode == "existing" || mode == "install" || mode == "login";
    RECT ownerRect{};
    GetWindowRect(owner, &ownerRect);
    int requestedLeft = std::atoi(GetArgument(args, "--left").c_str());
    int requestedTop = std::atoi(GetArgument(args, "--top").c_str());
    int x = requestedLeft != 0 ? requestedLeft : owner ? ownerRect.left + ((ownerRect.right - ownerRect.left - width) / 2) : CW_USEDEFAULT;
    int y = requestedTop != 0 ? requestedTop : owner ? ownerRect.top + ((ownerRect.bottom - ownerRect.top - height) / 2) : CW_USEDEFAULT;
    HWND window = CreateWindowExW(WS_EX_TOOLWINDOW, windowClass.lpszClassName, L"", WS_POPUP, x, y, width, height, owner, nullptr, instance, nullptr);
    if (!window || !CreateDevice(window)) return 1;
    const DWORD roundedCorners = 2; // DWMWCP_ROUND
    DwmSetWindowAttribute(window, 33, &roundedCorners, sizeof(roundedCorners)); // DWMWA_WINDOW_CORNER_PREFERENCE
    ShowWindow(window, SW_SHOW);
    UpdateWindow(window);

    IMGUI_CHECKVERSION();
    ImGui::CreateContext();
    ImGuiIO& io = ImGui::GetIO();
#if IMGUI_VERSION_NUM >= 19120
    io.ConfigDebugHighlightIdConflicts = false;
#endif
    wchar_t modulePath[MAX_PATH]{};
    GetModuleFileNameW(nullptr, modulePath, ARRAYSIZE(modulePath));
    std::wstring iniPath(modulePath);
    iniPath = iniPath.substr(0, iniPath.find_last_of(L"\\/")) + L"\\imgui.ini";
    std::string iniPathUtf8 = ToUtf8(iniPath);
    io.IniFilename = iniPathUtf8.c_str();
    std::wstring dependencyRoot(modulePath);
    dependencyRoot = dependencyRoot.substr(0, dependencyRoot.find_last_of(L"\\/"));
    std::wstring fontPath = dependencyRoot + L"\\Assets\\Fonts\\aller.regular.ttf";
    g_iconDirectory = dependencyRoot + L"\\Assets\\Icons\\";
    if (GetFileAttributesW(fontPath.c_str()) != INVALID_FILE_ATTRIBUTES)
        io.Fonts->AddFontFromFileTTF(ToUtf8(fontPath).c_str(), 16.0f);
    else
        io.Fonts->AddFontDefault();
    io.ConfigFlags |= ImGuiConfigFlags_NavEnableKeyboard;
    ImGui::StyleColorsDark();
    ImGuiStyle& style = ImGui::GetStyle();
    style.WindowRounding = 10.0f;
    style.ChildRounding = 12.0f;
    style.FrameRounding = 15.0f;
    style.GrabRounding = 15.0f;
    style.FrameBorderSize = 1.0f;
    style.WindowPadding = ImVec2(15, 14);
    style.FramePadding = ImVec2(9, 7);
    style.ItemSpacing = ImVec2(10, 10);
    style.Colors[ImGuiCol_WindowBg] = ImVec4(0.035f, 0.055f, 0.095f, 0.78f);
    style.Colors[ImGuiCol_TitleBg] = ImVec4(0.08f, 0.16f, 0.28f, 1.0f);
    style.Colors[ImGuiCol_TitleBgActive] = ImVec4(0.10f, 0.24f, 0.40f, 1.0f);
    // Match the launcher's dark secondary buttons and their pale hover overlay.
    style.Colors[ImGuiCol_Border] = ImVec4(0.49f, 0.58f, 0.74f, 0.65f);
    style.Colors[ImGuiCol_Button] = ImVec4(0.10f, 0.14f, 0.22f, 0.78f);
    style.Colors[ImGuiCol_ButtonHovered] = ImVec4(0.24f, 0.28f, 0.34f, 0.90f);
    style.Colors[ImGuiCol_ButtonActive] = ImVec4(0.30f, 0.34f, 0.40f, 0.96f);
    ImGui_ImplWin32_Init(window);
    ImGui_ImplDX11_Init(g_device, g_context);
    std::wstring backgroundPath = ToWide(GetArgument(args, "--background"));
    LoadBackgroundTexture(backgroundPath);

    char folder[1024]{}; char display[256]{}; char folderName[64]{}; char launchOptions[512]{}; char iconName[128]{}; char detectedText[256]{};
    strncpy_s(folder, GetArgument(args, "--folder").c_str(), _TRUNCATE);
    strncpy_s(display, GetArgument(args, "--display").c_str(), _TRUNCATE);
    strncpy_s(folderName, GetArgument(args, "--name").c_str(), _TRUNCATE);
    strncpy_s(launchOptions, GetArgument(args, "--launch").c_str(), _TRUNCATE);
    strncpy_s(iconName, GetArgument(args, "--icon").c_str(), _TRUNCATE);
    LoadSelectedIcon(iconName);
    const std::string id = GetArgument(args, "--id");
    const std::string detected = GetArgument(args, "--detected");
    const std::string installName = GetArgument(args, "--install-name");
    const std::string versionsArgument = GetArgument(args, "--versions");
    const std::string librariesArgument = GetArgument(args, "--libraries");
    const std::string settingsVersionsArgument = GetArgument(args, "--settings-versions");
    const std::string settingsEntriesArgument = GetArgument(args, "--settings-entries");
    const std::string modVersionsArgument = GetArgument(args, "--mod-versions");
    const std::string modInstalledArgument = GetArgument(args, "--mod-installed");
    const std::string modLoadersArgument = GetArgument(args, "--mod-loaders");
    std::string modSelectedPath = Decode(GetArgument(args, "--mod-selected"));
    std::string modStatus = Decode(GetArgument(args, "--mod-status"));
    std::string modWarning = Decode(GetArgument(args, "--mod-warning"));
    bool modFolderEnabled = GetArgument(args, "--mod-folder-enabled") == "1";
    std::vector<std::pair<std::string, std::string>> installVersions;
    size_t recordStart = 0;
    while (recordStart < versionsArgument.size())
    {
        size_t recordEnd = versionsArgument.find('\n', recordStart);
        std::string record = versionsArgument.substr(recordStart, recordEnd == std::string::npos ? std::string::npos : recordEnd - recordStart);
        size_t separator = record.find('\t');
        if (separator != std::string::npos) installVersions.emplace_back(record.substr(0, separator), record.substr(separator + 1));
        if (recordEnd == std::string::npos) break;
        recordStart = recordEnd + 1;
    }
    int selectedInstallVersion = -1;
    std::vector<std::string> installLibraries;
    recordStart = 0;
    while (recordStart < librariesArgument.size())
    {
        size_t recordEnd = librariesArgument.find('\n', recordStart);
        std::string record = librariesArgument.substr(recordStart, recordEnd == std::string::npos ? std::string::npos : recordEnd - recordStart);
        if (!record.empty()) installLibraries.push_back(record);
        if (recordEnd == std::string::npos) break;
        recordStart = recordEnd + 1;
    }
    std::vector<ManagedSettingsVersion> managedSettingsVersions;
    recordStart = 0;
    while (recordStart < settingsVersionsArgument.size())
    {
        size_t recordEnd = settingsVersionsArgument.find('\n', recordStart);
        std::string record = settingsVersionsArgument.substr(recordStart, recordEnd == std::string::npos ? std::string::npos : recordEnd - recordStart);
        size_t first = record.find('\t');
        size_t second = first == std::string::npos ? std::string::npos : record.find('\t', first + 1);
        if (first != std::string::npos && second != std::string::npos)
            managedSettingsVersions.push_back({ Decode(record.substr(0, first)), Decode(record.substr(first + 1, second - first - 1)), record.substr(second + 1) == "1" });
        if (recordEnd == std::string::npos) break;
        recordStart = recordEnd + 1;
    }
    std::vector<SavedSettingEntry> savedSettingEntries;
    recordStart = 0;
    while (recordStart < settingsEntriesArgument.size())
    {
        size_t recordEnd = settingsEntriesArgument.find('\n', recordStart);
        std::string record = settingsEntriesArgument.substr(recordStart, recordEnd == std::string::npos ? std::string::npos : recordEnd - recordStart);
        size_t first = record.find('\t');
        size_t second = first == std::string::npos ? std::string::npos : record.find('\t', first + 1);
        if (first != std::string::npos && second != std::string::npos)
            savedSettingEntries.push_back({ Decode(record.substr(0, first)), Decode(record.substr(first + 1, second - first - 1)), Decode(record.substr(second + 1)) });
        if (recordEnd == std::string::npos) break;
        recordStart = recordEnd + 1;
    }
    std::vector<ModWindowVersion> modVersions;
    recordStart = 0;
    while (recordStart < modVersionsArgument.size())
    {
        size_t recordEnd = modVersionsArgument.find('\n', recordStart);
        std::string record = modVersionsArgument.substr(recordStart, recordEnd == std::string::npos ? std::string::npos : recordEnd - recordStart);
        size_t separator = record.find('\t');
        if (separator != std::string::npos)
            modVersions.push_back({ Decode(record.substr(0, separator)), Decode(record.substr(separator + 1)) });
        if (recordEnd == std::string::npos) break;
        recordStart = recordEnd + 1;
    }
    auto parseModCards = [](const std::string& argument, bool loaderCards)
    {
        std::vector<ModWindowCard> cards;
        size_t start = 0;
        while (start < argument.size())
        {
            size_t end = argument.find('\n', start);
            std::string record = argument.substr(start, end == std::string::npos ? std::string::npos : end - start);
            std::vector<std::string> fields;
            size_t fieldStart = 0;
            while (fieldStart <= record.size())
            {
                size_t fieldEnd = record.find('\t', fieldStart);
                fields.push_back(record.substr(fieldStart, fieldEnd == std::string::npos ? std::string::npos : fieldEnd - fieldStart));
                if (fieldEnd == std::string::npos) break;
                fieldStart = fieldEnd + 1;
            }
            if ((!loaderCards && fields.size() >= 6) || (loaderCards && fields.size() >= 9))
            {
                ModWindowCard card;
                card.id = Decode(fields[0]);
                card.name = Decode(fields[1]);
                card.author = Decode(fields[2]);
                card.version = Decode(fields[3]);
                card.description = Decode(fields[4]);
                card.action = Decode(fields[5]);
                if (loaderCards)
                {
                    card.installed = fields[6] == "1";
                    card.canAction = fields[7] == "1";
                    card.unavailableReason = Decode(fields[8]);
                }
                else card.installed = true;
                cards.push_back(std::move(card));
            }
            if (end == std::string::npos) break;
            start = end + 1;
        }
        return cards;
    };
    std::vector<ModWindowCard> installedModCards = parseModCards(modInstalledArgument, false);
    std::vector<ModWindowCard> loaderModCards = parseModCards(modLoadersArgument, true);
    int selectedModVersion = 0;
    for (int index = 0; index < static_cast<int>(modVersions.size()); ++index)
        if (_stricmp(modVersions[index].path.c_str(), modSelectedPath.c_str()) == 0) selectedModVersion = index;
    bool showInstalledMods = true;
    bool showModRemovalConfirmation = false;
    bool modBusy = false;
    std::string modOperationError;
    std::string pendingModCommand;
    std::string pendingModId;
    std::string pendingModName;
    bool hasSavedSettings = GetArgument(args, "--settings-has-saved") == "1";
    bool applySavedSettings = GetArgument(args, "--settings-apply") != "0";
    const std::string savedSettingsSource = GetArgument(args, "--settings-source");
    const std::string settingsMessage = Decode(GetArgument(args, "--settings-message"));
    int settingsPage = 0;
    int selectedManagedVersion = -1;
    bool settingsSelectionIsCopy = false;
    char username[256]{}; char password[256]{}; char authInput[256]{};
    strncpy_s(username, GetArgument(args, "--username").c_str(), _TRUNCATE);
    std::string selectedLibraryArgument = GetArgument(args, "--selected-library");
    int selectedLibrary = 0;
    for (int index = 0; index < static_cast<int>(installLibraries.size()); ++index)
        if (_stricmp(installLibraries[index].c_str(), selectedLibraryArgument.c_str()) == 0) selectedLibrary = index;
    bool useRememberedLogin = GetArgument(args, "--use-remembered") == "1";
    bool rememberedLoginSeeded = GetArgument(args, "--remembered-seeded") == "1";
    if (!rememberedLoginSeeded) useRememberedLogin = false;
    bool rememberPassword = GetArgument(args, "--remember-password") == "1";
    bool preferCode = GetArgument(args, "--prefer-code") != "0";
    std::string loginError;
    std::string protocolPending;
    std::string progressStatus = "Preparing install...";
    std::string progressEta = "Estimated time remaining: Calculating...";
    std::vector<std::string> progressLog;
    float installProgress = 0.0f;
    bool installFinished = false;
    bool installSucceeded = false;
    bool promptVisible = false;
    bool promptSecret = false;
    bool promptAllowWaiting = false;
    bool promptDismissRequested = false;
    bool cancelConfirmationRequested = false;
    bool cancelRequested = false;
    bool newLogReceived = false;
    ULONGLONG automaticCloseAt = 0;
    std::string promptId;
    std::string promptText;
    strncpy_s(detectedText, detected.c_str(), _TRUNCATE);
    std::string result;
    bool showDetectionConfirmation = !detected.empty();

    while (!g_done)
    {
        MSG message;
        while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) { TranslateMessage(&message); DispatchMessageW(&message); }
        if (g_done) break;
        if (mode == "progress")
        {
            for (const std::string& line : PollProtocolInput(protocolPending))
            {
                size_t first = line.find('|');
                std::string command = first == std::string::npos ? line : line.substr(0, first);
                std::string payload = first == std::string::npos ? "" : line.substr(first + 1);
                if (command == "STATUS") progressStatus = Decode(payload);
                else if (command == "LOG")
                {
                    progressLog.push_back(Decode(payload));
                    if (progressLog.size() > 4000) progressLog.erase(progressLog.begin(), progressLog.begin() + 500);
                    newLogReceived = true;
                }
                else if (command == "PROGRESS") installProgress = std::clamp(static_cast<float>(std::atof(payload.c_str())), 0.0f, 100.0f);
                else if (command == "ETA") progressEta = Decode(payload);
                else if (command == "PROMPT")
                {
                    size_t second = payload.find('|');
                    size_t third = second == std::string::npos ? std::string::npos : payload.find('|', second + 1);
                    size_t fourth = third == std::string::npos ? std::string::npos : payload.find('|', third + 1);
                    if (second != std::string::npos && third != std::string::npos && fourth != std::string::npos)
                    {
                        promptId = payload.substr(0, second);
                        promptSecret = payload.substr(second + 1, third - second - 1) == "1";
                        promptAllowWaiting = payload.substr(third + 1, fourth - third - 1) == "1";
                        promptText = Decode(payload.substr(fourth + 1));
                        authInput[0] = '\0';
                        promptVisible = true;
                    }
                }
                else if (command == "DISMISS")
                {
                    if (payload.empty() || payload == promptId) promptDismissRequested = true;
                }
                else if (command == "COMPLETE")
                {
                    progressStatus = Decode(payload);
                    installProgress = 100.0f;
                    progressEta = "Estimated time remaining: Complete";
                    installFinished = installSucceeded = true;
                    promptDismissRequested = true;
                    automaticCloseAt = GetTickCount64() + 450;
                }
                else if (command == "CANCELLED")
                {
                    progressStatus = Decode(payload);
                    progressEta.clear();
                    installFinished = true;
                    installSucceeded = false;
                    promptDismissRequested = true;
                    automaticCloseAt = GetTickCount64() + 650;
                }
                else if (command == "FAILED")
                {
                    progressStatus = Decode(payload);
                    installFinished = true;
                    installSucceeded = false;
                    promptDismissRequested = true;
                }
            }
        }
        else if (mode == "mods")
        {
            for (const std::string& line : PollProtocolInput(protocolPending))
            {
                std::vector<std::string> fields;
                size_t start = 0;
                while (start <= line.size())
                {
                    size_t end = line.find('|', start);
                    fields.push_back(line.substr(start, end == std::string::npos ? std::string::npos : end - start));
                    if (end == std::string::npos) break;
                    start = end + 1;
                }
                if (fields.size() >= 7 && fields[0] == "MOD_DATA")
                {
                    modSelectedPath = Decode(fields[1]);
                    modStatus = Decode(fields[2]);
                    modWarning = Decode(fields[3]);
                    installedModCards = parseModCards(Decode(fields[4]), false);
                    loaderModCards = parseModCards(Decode(fields[5]), true);
                    modFolderEnabled = fields[6] == "1";
                    for (int index = 0; index < static_cast<int>(modVersions.size()); ++index)
                        if (_stricmp(modVersions[index].path.c_str(), modSelectedPath.c_str()) == 0) selectedModVersion = index;
                    modBusy = false;
                    modOperationError.clear();
                }
                else if (fields.size() >= 2 && fields[0] == "MOD_ERROR")
                {
                    modOperationError = Decode(fields[1]);
                    modBusy = false;
                }
            }
        }
        if (mode == "progress" && automaticCloseAt != 0 && GetTickCount64() >= automaticCloseAt)
        {
            WriteResult("CLOSE");
            g_done = true;
            continue;
        }
        if (g_resizeWidth && g_resizeHeight) { CleanupTarget(); g_swapChain->ResizeBuffers(0, g_resizeWidth, g_resizeHeight, DXGI_FORMAT_UNKNOWN, 0); g_resizeWidth = g_resizeHeight = 0; CreateTarget(); }
        ImGui_ImplDX11_NewFrame(); ImGui_ImplWin32_NewFrame(); ImGui::NewFrame();
        ImGui::SetNextWindowPos(ImVec2(0, 0));
        ImGui::SetNextWindowSize(ImGui::GetIO().DisplaySize);
        ImGuiWindowFlags flags = ImGuiWindowFlags_NoTitleBar | ImGuiWindowFlags_NoCollapse | ImGuiWindowFlags_NoMove | ImGuiWindowFlags_NoResize | ImGuiWindowFlags_NoScrollbar | ImGuiWindowFlags_NoSavedSettings;
        std::string dynamicTitle = mode == "progress" && !installName.empty() ? "Installing " + installName : "Installing Subnautica";
        const char* title = mode == "choice" ? "Add Version to Launcher" : mode == "edit" ? "Edit Version" : mode == "install" ? "Select a Version to Install" : mode == "login" ? "Login to Depot Downloader" : mode == "progress" ? dynamicTitle.c_str() : mode == "settings" ? "Subnautica In-Game Settings" : mode == "mods" ? "Manage Mods" : "Add Existing Version to Launcher";
        ImGui::Begin("##LauncherHost", nullptr, flags);
        if (g_background)
        {
            ImVec2 backgroundStart = ImGui::GetWindowPos();
            ImVec2 windowSize = ImGui::GetWindowSize();
            ImVec2 backgroundEnd(backgroundStart.x + windowSize.x, backgroundStart.y + windowSize.y);
            ImGui::GetWindowDrawList()->AddImage(reinterpret_cast<ImTextureID>(g_background), backgroundStart, backgroundEnd);
        }
        ImGuiWindow* contentWindow = ImGui::GetCurrentWindow();
        ImVec2 titleStart = ImGui::GetWindowPos();
        ImVec2 titleEnd(titleStart.x + ImGui::GetWindowWidth(), titleStart.y + kTitleBarHeight);
        contentWindow->DrawList->AddRectFilled(titleStart, titleEnd, IM_COL32(0, 0, 0, 150));
        contentWindow->DrawList->AddText(ImGui::GetFont(), 13.0f, ImVec2(titleStart.x + 10.0f, titleStart.y + 9.0f), IM_COL32(245, 248, 255, 255), title);
        const float closeX = ImGui::GetWindowWidth() - kTitleRightPadding - kTitleButtonSize;
        g_hasBackButton = mode == "existing" || mode == "install" || mode == "login" || (mode == "settings" && settingsPage != 0);
        if (g_hasBackButton)
        {
            if (TitleIconButton("##back", ImVec2(titleStart.x + closeX - kTitleButtonGap - kTitleButtonSize, titleStart.y + kTitleButtonTop), true))
            {
                if (mode == "login") { result = "BACK"; g_done = true; }
                else if (mode == "settings") { settingsPage = 0; selectedManagedVersion = -1; ResizeSettingsPage(window, settingsPage); }
                else { mode = "choice"; ResizeForMode(window, mode); }
            }
        }
        if (TitleIconButton("##close", ImVec2(titleStart.x + closeX, titleStart.y + kTitleButtonTop), false))
        {
            if (mode == "progress" && !installFinished) cancelConfirmationRequested = !cancelRequested;
            else g_done = true;
        }
        ImGui::SetCursorPos(ImVec2(15.0f, 39.0f));
        if (mode == "choice")
        {
            ImGui::Dummy(ImVec2(0, 26));
            float buttonWidth = 165.0f;
            float start = (ImGui::GetContentRegionAvail().x - (buttonWidth * 2.0f + 10.0f)) * 0.5f;
            ImGui::SetCursorPosX(ImGui::GetCursorPosX() + (std::max)(0.0f, start));
            if (LauncherButton("Add Existing Version", ImVec2(buttonWidth, 42))) { mode = "existing"; ResizeForMode(window, mode); }
            ImGui::SameLine();
            if (LauncherButton("Install New Version", ImVec2(buttonWidth, 42))) { mode = "install"; ResizeForMode(window, mode); }
        }
        else if (mode == "mods")
        {
            ImGui::TextUnformatted("Installed Version");
            ImGui::SameLine(170.0f);
            ImGui::SetNextItemWidth(-1.0f);
            const char* selectedVersionName = modVersions.empty() ? "No managed versions" : modVersions[selectedModVersion].displayName.c_str();
            if (ImGui::BeginCombo("##mod-version", selectedVersionName))
            {
                for (int index = 0; index < static_cast<int>(modVersions.size()); ++index)
                {
                    bool selectedItem = index == selectedModVersion;
                    if (ImGui::Selectable(modVersions[index].displayName.c_str(), selectedItem) && index != selectedModVersion)
                    {
                        selectedModVersion = index;
                        modStatus = "Loading mod information...";
                        modWarning.clear();
                        modOperationError.clear();
                        installedModCards.clear();
                        loaderModCards.clear();
                        modFolderEnabled = false;
                        modBusy = true;
                        WriteResult("MOD_SELECT|" + Encode(modVersions[index].path));
                    }
                    if (selectedItem) ImGui::SetItemDefaultFocus();
                }
                ImGui::EndCombo();
            }
            if (ImGui::IsItemHovered()) ImGui::SetMouseCursor(ImGuiMouseCursor_Hand);
            ImGui::Dummy(ImVec2(0, 3));
            ImGui::TextWrapped("%s", modStatus.c_str());
            if (!modOperationError.empty())
            {
                ImGui::PushStyleColor(ImGuiCol_Text, ImVec4(1.0f, 0.55f, 0.55f, 1.0f));
                ImGui::TextWrapped("%s", modOperationError.c_str());
                ImGui::PopStyleColor();
            }

            if (!modWarning.empty())
            {
                ImGui::PushStyleColor(ImGuiCol_ChildBg, ImVec4(0.32f, 0.09f, 0.07f, 0.88f));
                ImGui::BeginChild("##mod-warning", ImVec2(-1, 82), true);
                ImGui::PushTextWrapPos(ImGui::GetContentRegionAvail().x - 150.0f);
                ImGui::TextWrapped("%s", modWarning.c_str());
                ImGui::PopTextWrapPos();
                ImGui::SetCursorPos(ImVec2(ImGui::GetWindowWidth() - 145.0f, 24.0f));
                ImGui::BeginDisabled(modBusy);
                if (LauncherButton("Remove Conflict", ImVec2(125, 34)))
                {
                    pendingModCommand = "MOD_REMOVE_INCOMPATIBLE";
                    pendingModId.clear();
                    pendingModName = "incompatible BepInEx and QModManager files";
                    showModRemovalConfirmation = true;
                }
                ImGui::EndDisabled();
                ImGui::EndChild();
                ImGui::PopStyleColor();
            }

            ImGui::Dummy(ImVec2(0, 2));
            std::string installedLabel = "Installed (" + std::to_string(installedModCards.size()) + ")";
            bool installedTabActive = showInstalledMods;
            if (installedTabActive) ImGui::PushStyleColor(ImGuiCol_Button, ImVec4(0.10f, 0.44f, 0.24f, 0.40f));
            if (LauncherButton((installedLabel + "##installed-mods").c_str(), ImVec2(150, 34))) showInstalledMods = true;
            if (installedTabActive) ImGui::PopStyleColor();
            ImGui::SameLine();
            bool loaderTabActive = !showInstalledMods;
            if (loaderTabActive) ImGui::PushStyleColor(ImGuiCol_Button, ImVec4(0.10f, 0.44f, 0.24f, 0.40f));
            if (LauncherButton("Mod Loaders##mod-loaders", ImVec2(150, 34))) showInstalledMods = false;
            if (loaderTabActive) ImGui::PopStyleColor();
            ImGui::Separator();

            ImGui::PushStyleColor(ImGuiCol_ChildBg, ImVec4(16.0f / 255.0f, 23.0f / 255.0f, 37.0f / 255.0f, 168.0f / 255.0f));
            ImGui::BeginChild("##mod-card-list", ImVec2(-1, -1), true);
            const std::vector<ModWindowCard>& cards = showInstalledMods ? installedModCards : loaderModCards;
            if (cards.empty())
            {
                ImGui::SetCursorPosY(ImGui::GetCursorPosY() + 22.0f);
                ImGui::TextDisabled(showInstalledMods ? "No direct mods are installed." : "No supported mod loaders are available for this version.");
            }
            for (int index = 0; index < static_cast<int>(cards.size()); ++index)
            {
                const ModWindowCard& card = cards[index];
                std::string childId = "##mod-card-" + std::to_string(index) + (showInstalledMods ? "-installed" : "-loader");
                ImGui::PushStyleColor(ImGuiCol_ChildBg, ImVec4(16.0f / 255.0f, 23.0f / 255.0f, 37.0f / 255.0f, 138.0f / 255.0f));
                ImGui::PushStyleColor(ImGuiCol_Border, ImVec4(124.0f / 255.0f, 149.0f / 255.0f, 189.0f / 255.0f, 69.0f / 255.0f));
                ImGui::BeginChild(childId.c_str(), ImVec2(-1, 154), true);
                ImGui::SetWindowFontScale(1.18f);
                ImGui::TextUnformatted(card.name.c_str());
                ImGui::SetWindowFontScale(0.82f);
                ImGui::TextDisabled("%s", card.author.c_str());
                ImGui::SetWindowFontScale(0.88f);
                ImGui::PushTextWrapPos(ImGui::GetWindowWidth() - 145.0f);
                ImGui::TextWrapped("%s", card.description.c_str());
                ImGui::PopTextWrapPos();
                ImGui::SetWindowFontScale(0.82f);
                ImGui::SetCursorPos(ImVec2(ImGui::GetWindowWidth() - 125.0f, 13.0f));
                ImGui::TextDisabled("Version %s", card.version.c_str());
                ImGui::SetWindowFontScale(1.0f);
                ImGui::SetCursorPos(ImVec2(ImGui::GetWindowWidth() - 125.0f, 40.0f));
                ImGui::BeginDisabled(!card.canAction || modBusy);
                if (LauncherButton((card.action + "##mod-action-" + std::to_string(index)).c_str(), ImVec2(105, 34)))
                {
                    if (card.action == "Remove")
                    {
                        pendingModCommand = showInstalledMods ? "MOD_REMOVE" : "MOD_REMOVE_LOADER";
                        pendingModId = card.id;
                        pendingModName = card.name;
                        showModRemovalConfirmation = true;
                    }
                    else
                    {
                        modBusy = true;
                        modOperationError.clear();
                        WriteResult("MOD_INSTALL|" + Encode(card.id));
                    }
                }
                ImGui::EndDisabled();
                if (!card.canAction && !card.unavailableReason.empty())
                {
                    ImGui::SetWindowFontScale(0.82f);
                    ImGui::SetCursorPos(ImVec2(15.0f, 126.0f));
                    ImGui::PushTextWrapPos(ImGui::GetWindowWidth() - 15.0f);
                    ImGui::TextDisabled("%s", card.unavailableReason.c_str());
                    ImGui::PopTextWrapPos();
                    ImGui::SetWindowFontScale(1.0f);
                }
                ImGui::EndChild();
                ImGui::PopStyleColor();
                ImGui::PopStyleColor();
                ImGui::Dummy(ImVec2(0, 5));
            }

            if (showInstalledMods && modFolderEnabled)
            {
                ImGui::PushStyleColor(ImGuiCol_ChildBg, ImVec4(16.0f / 255.0f, 23.0f / 255.0f, 37.0f / 255.0f, 138.0f / 255.0f));
                ImGui::PushStyleColor(ImGuiCol_Border, ImVec4(124.0f / 255.0f, 149.0f / 255.0f, 189.0f / 255.0f, 69.0f / 255.0f));
                ImGui::BeginChild("##bepinex-folder-card", ImVec2(-1, 92), true);
                ImGui::TextUnformatted("Install More BepInEx Mods");
                ImGui::PushTextWrapPos(ImGui::GetWindowWidth() - 150.0f);
                ImGui::TextWrapped("Add compatible mod files directly to the BepInEx plugins folder.");
                ImGui::PopTextWrapPos();
                ImGui::SetCursorPos(ImVec2(ImGui::GetWindowWidth() - 125.0f, 28.0f));
                ImGui::BeginDisabled(modBusy);
                if (LauncherButton("Open Folder", ImVec2(105, 34))) { modBusy = true; WriteResult("MOD_OPEN_FOLDER"); }
                ImGui::EndDisabled();
                ImGui::EndChild();
                ImGui::PopStyleColor();
                ImGui::PopStyleColor();
            }
            ImGui::EndChild();
            ImGui::PopStyleColor();
        }
#if 0 // Plugin settings are now the plugin's native LiveSplit WinForms control.
        else if (mode == "plugin-settings")
        {
            const char* gameModes[] = {
                "Survival (Vanilla)", "Hardcore (Vanilla)", "Creative (Vanilla)", "Freedom (Vanilla)",
                "BetterRNG Survival (Speedrun Mod)", "BetterRNG Hardcore (Speedrun Mod)", "BetterRNG Creative (Speedrun Mod)"
            };
            const char* explosionPresets[] = {
                "46:00 - 46:30", "46:00 - 47:00", "46:00 - 48:00", "46:00 - 50:00",
                "Under 1 Hour", "Over 1 Hour", "Custom"
            };
            if (ImGui::Checkbox("Enable Reset Macro", &pluginMacroEnabled) && !pluginMacroEnabled) g_capturePluginHotkey = false;
            if (ImGui::IsItemHovered()) ImGui::SetMouseCursor(ImGuiMouseCursor_Hand);
            ImGui::BeginDisabled(!pluginMacroEnabled);
            ImGui::TextUnformatted("Reset Hotkey");
            ImGui::SameLine(185.0f);
            std::string hotkeyLabel = g_capturePluginHotkey ? "Press a key..." : FormatHotkey(g_pluginCapturedHotkey);
            if (LauncherButton((hotkeyLabel + "##plugin-hotkey").c_str(), ImVec2(280, 34))) g_capturePluginHotkey = true;
            ImGui::TextUnformatted("Game Mode");
            ImGui::SameLine(185.0f);
            ImGui::SetNextItemWidth(280.0f);
            if (ImGui::BeginCombo("##plugin-game-mode", gameModes[pluginGameMode]))
            {
                for (int index = 0; index < 7; ++index)
                {
                    if (ImGui::Selectable(gameModes[index], pluginGameMode == index)) pluginGameMode = index;
                    if (pluginGameMode == index) ImGui::SetItemDefaultFocus();
                }
                ImGui::EndCombo();
            }
            if (ImGui::IsItemHovered()) ImGui::SetMouseCursor(ImGuiMouseCursor_Hand);
            ImGui::Dummy(ImVec2(0, 8));
            ImGui::EndDisabled();

            ImGui::BeginDisabled(!pluginMacroEnabled);
            ImGui::Checkbox("Explosion Reset Macro", &pluginExplosionReset);
            if (ImGui::IsItemHovered()) ImGui::SetMouseCursor(ImGuiMouseCursor_Hand);
            ImGui::EndDisabled();
            ImGui::BeginDisabled(!pluginMacroEnabled || !pluginExplosionReset);
            ImGui::TextUnformatted("Explosion Preset");
            ImGui::SameLine(185.0f);
            ImGui::SetNextItemWidth(280.0f);
            if (ImGui::BeginCombo("##plugin-explosion-preset", explosionPresets[pluginExplosionPreset]))
            {
                for (int index = 0; index < 7; ++index)
                {
                    if (ImGui::Selectable(explosionPresets[index], pluginExplosionPreset == index)) pluginExplosionPreset = index;
                    if (pluginExplosionPreset == index) ImGui::SetItemDefaultFocus();
                }
                ImGui::EndCombo();
            }
            if (ImGui::IsItemHovered()) ImGui::SetMouseCursor(ImGuiMouseCursor_Hand);
            if (pluginExplosionPreset == 6)
            {
                ImGui::TextUnformatted("Custom Range");
                ImGui::SameLine(185.0f);
                ImGui::SetNextItemWidth(115.0f);
                ImGui::InputText("##plugin-custom-min", pluginCustomMinimum, sizeof(pluginCustomMinimum));
                ImGui::SameLine();
                ImGui::TextUnformatted("to");
                ImGui::SameLine();
                ImGui::SetNextItemWidth(115.0f);
                ImGui::InputText("##plugin-custom-max", pluginCustomMaximum, sizeof(pluginCustomMaximum));
            }
            ImGui::EndDisabled();
            ImGui::Dummy(ImVec2(0, 9));
            ImGui::Checkbox("Hardcore Save Deleter", &pluginSaveDeleter);
            if (ImGui::IsItemHovered()) ImGui::SetMouseCursor(ImGuiMouseCursor_Hand);
            ImGui::BeginDisabled(!pluginSaveDeleter);
            const char* deletionModes[] = { "Hardcore", "All Gamemodes" };
            const char* deletionTimes[] = { "After Reset Completes", "Automatically Delete all Saves" };
            ImGui::TextUnformatted("Gamemode");
            ImGui::SameLine(185.0f);
            ImGui::SetNextItemWidth(280.0f);
            if (ImGui::BeginCombo("##plugin-save-game-mode", deletionModes[pluginSaveGameMode]))
            {
                for (int index = 0; index < 2; ++index)
                {
                    if (ImGui::Selectable(deletionModes[index], pluginSaveGameMode == index)) pluginSaveGameMode = index;
                    if (pluginSaveGameMode == index) ImGui::SetItemDefaultFocus();
                }
                ImGui::EndCombo();
            }
            ImGui::TextUnformatted("When to Delete");
            ImGui::SameLine(185.0f);
            ImGui::SetNextItemWidth(280.0f);
            if (ImGui::BeginCombo("##plugin-save-timing", deletionTimes[pluginSaveTiming]))
            {
                for (int index = 0; index < 2; ++index)
                {
                    if (ImGui::Selectable(deletionTimes[index], pluginSaveTiming == index)) pluginSaveTiming = index;
                    if (pluginSaveTiming == index) ImGui::SetItemDefaultFocus();
                }
                ImGui::EndCombo();
            }
            ImGui::EndDisabled();
            ImGui::SetCursorPos(ImVec2((ImGui::GetWindowWidth() - 150.0f) * 0.5f, ImGui::GetWindowHeight() - 58.0f));
            if (LauncherButton("Save Settings", ImVec2(150, 36)))
            {
                result = std::string("PLUGIN_SETTINGS|") + (pluginMacroEnabled ? "1" : "0") + "|"
                    + std::to_string(g_pluginCapturedHotkey) + "|" + std::to_string(pluginGameMode) + "|"
                    + (pluginExplosionReset ? "1" : "0") + "|" + std::to_string(pluginExplosionPreset) + "|"
                    + pluginCustomMinimum + "|" + pluginCustomMaximum + "|"
                    + (pluginSaveDeleter ? "1" : "0") + "|" + std::to_string(pluginSaveGameMode) + "|"
                    + std::to_string(pluginSaveTiming);
                g_done = true;
            }
        }
#endif
        else if (mode == "install")
        {
            ImGui::PushStyleColor(ImGuiCol_ChildBg, ImVec4(16.0f / 255.0f, 23.0f / 255.0f, 37.0f / 255.0f, 168.0f / 255.0f));
            ImGui::BeginChild("##version-list", ImVec2(370, 286), true);
            const auto isEarlyAccessVersion = [](const std::string& displayName)
            {
                return displayName.rfind("*Early Access*", 0) == 0;
            };
            bool hasEarlyAccessVersions = false;
            for (int index = 0; index < static_cast<int>(installVersions.size()); ++index)
            {
                if (isEarlyAccessVersion(installVersions[index].second))
                {
                    hasEarlyAccessVersions = true;
                    continue;
                }
                ImGui::PushID(index);
                bool selected = selectedInstallVersion == index;
                if (ImGui::Selectable(installVersions[index].second.c_str(), selected)) selectedInstallVersion = index;
                if (ImGui::IsItemHovered()) ImGui::SetMouseCursor(ImGuiMouseCursor_Hand);
                ImGui::PopID();
            }
            if (hasEarlyAccessVersions)
            {
                ImGui::Spacing();
                ImGui::Separator();
                ImGui::Spacing();
                ImGui::SetNextItemOpen(false, ImGuiCond_Once);

                bool earlyAccessExpanded = ImGui::CollapsingHeader("Early Access Builds");

                if (ImGui::IsItemHovered())
                   ImGui::SetMouseCursor(ImGuiMouseCursor_Hand);

                if (earlyAccessExpanded)
             {
                    for (int index = 0; index < static_cast<int>(installVersions.size()); ++index)
                  {
                      if (!isEarlyAccessVersion(installVersions[index].second)) continue;

                      ImGui::PushID(index);

                     bool selected = selectedInstallVersion == index;

                        if (ImGui::Selectable(installVersions[index].second.c_str(), selected))
                         selectedInstallVersion = index;

                        if (ImGui::IsItemHovered())
                         ImGui::SetMouseCursor(ImGuiMouseCursor_Hand);

                       ImGui::PopID();
                  }
               }
            }
            ImGui::EndChild();
            ImGui::PopStyleColor();
            ImGui::SetCursorPos(ImVec2(125, 342));
            ImGui::BeginDisabled(selectedInstallVersion < 0);
            if (LauncherButton("Install", ImVec2(150, 36)) && selectedInstallVersion >= 0)
            {
                result = "INSTALL|" + Encode(installVersions[selectedInstallVersion].first);
                g_done = true;
            }
            ImGui::EndDisabled();
        }
        else if (mode == "login")
        {
            ImGui::TextUnformatted("Steam Username");
            ImGui::SetNextItemWidth(-1);
            ImGui::InputText("##username", username, sizeof(username));
            ImGui::TextUnformatted("Steam Password");
            ImGui::SetNextItemWidth(-1);
            ImGui::BeginDisabled(useRememberedLogin);
            ImGui::InputText("##password", password, sizeof(password), ImGuiInputTextFlags_Password);
            ImGui::EndDisabled();
            ImGui::TextUnformatted("Install Library");
            ImGui::SetNextItemWidth(-1);
            const char* libraryPreview = installLibraries.empty() ? "No Steam libraries detected" : installLibraries[selectedLibrary].c_str();
            if (ImGui::BeginCombo("##library", libraryPreview))
            {
                for (int index = 0; index < static_cast<int>(installLibraries.size()); ++index)
                {
                    bool selected = selectedLibrary == index;
                    if (ImGui::Selectable(installLibraries[index].c_str(), selected)) selectedLibrary = index;
                    if (ImGui::IsItemHovered()) ImGui::SetMouseCursor(ImGuiMouseCursor_Hand);
                    if (selected) ImGui::SetItemDefaultFocus();
                }
                ImGui::EndCombo();
            }
            if (ImGui::IsItemHovered()) ImGui::SetMouseCursor(ImGuiMouseCursor_Hand);
            ImGui::Dummy(ImVec2(0, 4));
            if (rememberedLoginSeeded)
                ImGui::Checkbox("Use remembered login for this install", &useRememberedLogin);
            else
                ImGui::Checkbox("Remember Login for next Install?", &rememberPassword);
            if (ImGui::IsItemHovered()) ImGui::SetMouseCursor(ImGuiMouseCursor_Hand);
            ImGui::Checkbox("Prefer Steam Guard code entry (-no-mobile)", &preferCode);
            if (ImGui::IsItemHovered()) ImGui::SetMouseCursor(ImGuiMouseCursor_Hand);
            if (!loginError.empty())
            {
                ImGui::PushStyleColor(ImGuiCol_Text, ImVec4(1.0f, 0.55f, 0.25f, 1.0f));
                ImGui::TextWrapped("%s", loginError.c_str());
                ImGui::PopStyleColor();
            }
            ImGui::SetCursorPos(ImVec2(165, 430));
            if (LauncherButton("Continue", ImVec2(150, 36)))
            {
                loginError.clear();
                if (username[0] == '\0') loginError = "Steam username is required.";
                else if (installLibraries.empty()) loginError = "No valid Steam library was detected.";
                else if (!useRememberedLogin && password[0] == '\0') loginError = "Steam password is required unless remembered login is enabled.";
                else
                {
                    bool shouldRemember = useRememberedLogin || (!rememberedLoginSeeded && rememberPassword);
                    result = "LOGIN|" + Encode(username) + "|" + Encode(useRememberedLogin ? "" : password) + "|" + Encode(installLibraries[selectedLibrary]) + "|" + (shouldRemember ? "1" : "0") + "|" + (preferCode ? "1" : "0") + "|" + (useRememberedLogin ? "1" : "0");
                    g_done = true;
                }
            }
        }
        else if (mode == "progress")
        {
            ImGui::TextWrapped("%s", progressStatus.c_str());
            ImGui::ProgressBar(installProgress / 100.0f, ImVec2(-1, 24), (std::to_string(static_cast<int>(installProgress)) + "%").c_str());
            ImGui::TextUnformatted(progressEta.c_str());
            ImGui::TextUnformatted("Install Log");
            ImGui::PushStyleColor(ImGuiCol_ChildBg, ImVec4(16.0f / 255.0f, 23.0f / 255.0f, 37.0f / 255.0f, 168.0f / 255.0f));
            ImGui::BeginChild("##install-log", ImVec2(-1, 360), true, ImGuiWindowFlags_HorizontalScrollbar);
            bool wasAtBottom = ImGui::GetScrollY() >= ImGui::GetScrollMaxY() - 8.0f;
            for (const std::string& line : progressLog) ImGui::TextUnformatted(line.c_str());
            if (newLogReceived && wasAtBottom) ImGui::SetScrollHereY(1.0f);
            newLogReceived = false;
            ImGui::EndChild();
            ImGui::PopStyleColor();
            ImGui::SetCursorPos(ImVec2(305, 510));
            const char* action = installFinished ? "Close" : cancelRequested ? "Cancelling..." : "Cancel Install";
            ImGui::BeginDisabled(cancelRequested);
            if (LauncherButton(action, ImVec2(150, 36)))
            {
                if (installFinished) { WriteResult("CLOSE"); g_done = true; }
                else cancelConfirmationRequested = true;
            }
            ImGui::EndDisabled();
        }
        else if (mode == "settings")
        {
            if (settingsPage == 0)
            {
                ImGui::TextUnformatted("Saved Settings");
                ImGui::Dummy(ImVec2(0, 4));
                if (hasSavedSettings)
                {
                    ImGui::TextWrapped("Saved Settings from %s", savedSettingsSource.empty() ? "Unknown Version" : savedSettingsSource.c_str());
                    if (LauncherButton("View", ImVec2(120, 36))) { settingsPage = 2; ResizeSettingsPage(window, settingsPage); }
                    ImGui::SameLine();
                    if (LauncherButton("Replace", ImVec2(120, 36))) { settingsSelectionIsCopy = false; settingsPage = 1; selectedManagedVersion = -1; ResizeSettingsPage(window, settingsPage); }
                }
                else
                {
                    ImGui::TextWrapped("No Subnautica settings have been saved yet.");
                    if (LauncherButton("Select Settings", ImVec2(160, 36))) { settingsSelectionIsCopy = false; settingsPage = 1; selectedManagedVersion = -1; ResizeSettingsPage(window, settingsPage); }
                }

                ImGui::Dummy(ImVec2(0, 12));
                ImGui::Separator();
                ImGui::Dummy(ImVec2(0, 12));
                ImGui::TextWrapped("Apply These Settings on New Version Installs");
                ImGui::PushStyleColor(ImGuiCol_Button, applySavedSettings ? ImVec4(0.10f, 0.55f, 0.28f, 0.92f) : ImVec4(0.67f, 0.18f, 0.20f, 0.92f));
                if (LauncherButton(applySavedSettings ? "Enabled" : "Disabled", ImVec2(150, 36)))
                {
                    result = std::string("SETTINGS_TOGGLE|") + (applySavedSettings ? "0" : "1");
                    g_done = true;
                }
                ImGui::PopStyleColor();
                ImGui::Dummy(ImVec2(0, 10));
                ImGui::BeginDisabled(!hasSavedSettings);
                if (LauncherButton("Copy Settings to Version", ImVec2(220, 36))) { settingsSelectionIsCopy = true; settingsPage = 1; selectedManagedVersion = -1; ResizeSettingsPage(window, settingsPage); }
                ImGui::EndDisabled();
                if (!settingsMessage.empty())
                {
                    ImGui::Dummy(ImVec2(0, 12));
                    ImGui::PushStyleColor(ImGuiCol_Text, ImVec4(0.76f, 0.88f, 1.0f, 1.0f));
                    ImGui::TextWrapped("%s", settingsMessage.c_str());
                    ImGui::PopStyleColor();
                }
            }
            else if (settingsPage == 1)
            {
                ImGui::TextWrapped("%s", settingsSelectionIsCopy ? "Choose a managed version to receive the saved settings." : "Choose the managed version whose settings should be saved.");
                ImGui::Dummy(ImVec2(0, 12));
                ImGui::TextUnformatted("Managed Version");
                ImGui::SetNextItemWidth(-1);
                const char* preview = selectedManagedVersion >= 0 ? managedSettingsVersions[selectedManagedVersion].displayName.c_str() : "Select a version";
                if (ImGui::BeginCombo("##managed-settings-version", preview))
                {
                    for (int index = 0; index < static_cast<int>(managedSettingsVersions.size()); ++index)
                    {
                        bool selected = selectedManagedVersion == index;
                        std::string label = managedSettingsVersions[index].displayName;
                        if (!settingsSelectionIsCopy && !managedSettingsVersions[index].hasSettings) label += " (No saved options)";
                        if (ImGui::Selectable(label.c_str(), selected)) selectedManagedVersion = index;
                        if (ImGui::IsItemHovered()) ImGui::SetMouseCursor(ImGuiMouseCursor_Hand);
                        if (selected) ImGui::SetItemDefaultFocus();
                    }
                    ImGui::EndCombo();
                }
                if (ImGui::IsItemHovered()) ImGui::SetMouseCursor(ImGuiMouseCursor_Hand);
                if (managedSettingsVersions.empty()) ImGui::TextWrapped("No managed Subnautica versions are available.");
                else if (selectedManagedVersion >= 0 && !settingsSelectionIsCopy && !managedSettingsVersions[selectedManagedVersion].hasSettings)
                    ImGui::TextWrapped("This version has not created SNAppData/SavedGames/options/options.bin yet.");

                bool validSelection = selectedManagedVersion >= 0 && (settingsSelectionIsCopy || managedSettingsVersions[selectedManagedVersion].hasSettings);
                const float actionWidth = 180.0f;
                ImGui::SetCursorPos(ImVec2((ImGui::GetWindowWidth() - actionWidth) * 0.5f, ImGui::GetWindowHeight() - 55.0f));
                ImGui::BeginDisabled(!validSelection);
                if (LauncherButton(settingsSelectionIsCopy ? "Copy Settings" : "Save Settings", ImVec2(actionWidth, 36)) && validSelection)
                {
                    result = std::string(settingsSelectionIsCopy ? "SETTINGS_COPY|" : "SETTINGS_SAVE|") + Encode(managedSettingsVersions[selectedManagedVersion].path);
                    g_done = true;
                }
                ImGui::EndDisabled();
            }
            else
            {
                ImGui::TextWrapped("Saved Settings from %s", savedSettingsSource.empty() ? "Unknown Version" : savedSettingsSource.c_str());
                ImGui::Dummy(ImVec2(0, 6));
                ImGui::PushStyleColor(ImGuiCol_ChildBg, ImVec4(16.0f / 255.0f, 23.0f / 255.0f, 37.0f / 255.0f, 168.0f / 255.0f));
                ImGui::BeginChild("##saved-settings-list", ImVec2(-1, -1), true);
                if (ImGui::BeginTable("##settings-table", 2, ImGuiTableFlags_RowBg | ImGuiTableFlags_BordersInnerV | ImGuiTableFlags_Resizable))
                {
                    ImGui::TableSetupColumn("Setting", ImGuiTableColumnFlags_WidthStretch, 0.62f);
                    ImGui::TableSetupColumn("Value", ImGuiTableColumnFlags_WidthStretch, 0.38f);
                    ImGui::TableHeadersRow();
                    std::string lastCategory;
                    for (const SavedSettingEntry& entry : savedSettingEntries)
                    {
                        if (entry.category != lastCategory)
                        {
                            ImGui::TableNextRow();
                            ImGui::TableSetColumnIndex(0);
                            ImGui::TextUnformatted(entry.category.c_str());
                            lastCategory = entry.category;
                        }
                        ImGui::TableNextRow();
                        ImGui::TableSetColumnIndex(0);
                        ImGui::TextWrapped("%s", entry.name.c_str());
                        ImGui::TableSetColumnIndex(1); ImGui::TextWrapped("%s", entry.value.c_str());
                    }
                    ImGui::EndTable();
                }
                ImGui::EndChild();
                ImGui::PopStyleColor();
            }
        }
        else if (mode == "existing")
        {
            ImGui::TextUnformatted("Folder (Must Contain \"Subnautica.exe\")");
            ImGui::SetNextItemWidth(-94);
            ImGui::InputText("##folder", folder, sizeof(folder));
            bool detectTypedFolder = ImGui::IsItemDeactivatedAfterEdit() && folder[0] != '\0';
            ImGui::SameLine();
            bool selectedFolder = LauncherButton("Browse", ImVec2(84, 0)) && BrowseForSubnauticaExecutable(folder, sizeof(folder));
            if ((detectTypedFolder || selectedFolder) && detected.empty()) { result = "DETECT|" + Encode(folder); g_done = true; }

            if (detected.empty())
            {
                ImGui::Dummy(ImVec2(0, 10));
                ImGui::BeginDisabled();
                ImGui::TextUnformatted("Display Name");
                ImGui::InputText("##display", display, sizeof(display));
                ImGui::TextUnformatted("Folder Name (not \"Subnautica\")");
                ImGui::InputText("##name", folderName, sizeof(folderName));
                ImGui::Dummy(ImVec2(0, 24));
                LauncherButton("Add Version", ImVec2(150, 34));
                ImGui::EndDisabled();
            }
            else
            {
                ImGui::Dummy(ImVec2(0, 10));
                ImGui::TextUnformatted("Display Name"); ImGui::InputText("##display", display, sizeof(display));
                ImGui::TextUnformatted("Folder Name (not \"Subnautica\")"); ImGui::InputText("##name", folderName, sizeof(folderName));
                ImGui::Dummy(ImVec2(0, 24));
                if (LauncherButton("Add Version", ImVec2(150, 34)) && folder[0] && display[0] && folderName[0]) { result = "ADD|" + Encode(folder) + "|" + Encode(display) + "|" + Encode(folderName) + "|" + Encode(id) + "|" + Encode(detected); g_done = true; }
                ImGui::SameLine();
                if (LauncherButton("Clear", ImVec2(96, 34))) { result = "CHOICE|existing"; g_done = true; }
            }
        }
        else if (mode == "edit")
        {
            const float fieldWidth = 365.0f;
            const float previewX = 452.0f;
            if (g_icon)
            {
                ImGui::SetCursorPos(ImVec2(previewX, 50.0f));
                ImGui::Image(reinterpret_cast<ImTextureID>(g_icon), ImVec2(82.0f, 82.0f));
            }
            ImGui::SetCursorPos(ImVec2(20.0f, 45.0f));
            ImGui::TextUnformatted("Display Name");
            ImGui::SetNextItemWidth(fieldWidth);
            ImGui::InputText("##display", display, sizeof(display));
            ImGui::SetCursorPos(ImVec2(20.0f, 120.0f));
            ImGui::TextUnformatted("Folder Name (not \"Subnautica\")");
            ImGui::SetNextItemWidth(fieldWidth);
            ImGui::InputText("##name", folderName, sizeof(folderName));
            ImGui::SetCursorPos(ImVec2(20.0f, 195.0f));
            ImGui::TextUnformatted("Launch Options");
            ImGui::SetNextItemWidth(fieldWidth);
            ImGui::InputText("##launch", launchOptions, sizeof(launchOptions));
            ImGui::SetCursorPos(ImVec2(444.0f, 160.0f));
            ImGui::TextUnformatted("Change Icon");
            ImGui::SetCursorPos(ImVec2(425.0f, 180.0f));
            ImGui::SetNextItemWidth(150);
            bool iconPickerOpen = ImGui::BeginCombo("##icon", GetIconDisplayName(iconName));
            if (ImGui::IsItemHovered()) ImGui::SetMouseCursor(ImGuiMouseCursor_Hand);
            if (iconPickerOpen)
            {
                const char* icons[] = { "SNLIcon.png", "SNLIconBZ.png", "Gold.png", "Titanium.png", "Kyanite.png", "StalkerTooth.png", "HatchingEnzymes.png", "IonCube.png" };
                for (const char* icon : icons)
                {
                    bool selected = std::strcmp(iconName, icon) == 0;
                    if (ImGui::Selectable(GetIconDisplayName(icon), selected)) { strncpy_s(iconName, sizeof(iconName), icon, _TRUNCATE); LoadSelectedIcon(iconName); }
                    if (selected) ImGui::SetItemDefaultFocus();
                }
                ImGui::EndCombo();
            }
            ImGui::SetCursorPos(ImVec2(20.0f, 260.0f));
            if (LauncherButton("Detect Steam Launch Options", ImVec2(210, 32)))
            {
                std::string detectedOptions = FindSteamLaunchOptions();
                if (!detectedOptions.empty()) strncpy_s(launchOptions, sizeof(launchOptions), detectedOptions.c_str(), _TRUNCATE);
                else ImGui::OpenPopup("No Steam Launch Options");
            }
            // Use the editor's full width rather than the remaining content width so this stays centered.
            float buttonWidth = 130.0f;
            float buttonsWidth = buttonWidth * 2.0f + 10.0f;
            ImGui::SetCursorPos(ImVec2((600.0f - buttonsWidth) * 0.5f, 360.0f));
            if (LauncherButton("Save", ImVec2(buttonWidth, 36)) && display[0] && folderName[0])
            {
                result = "EDIT|" + Encode(display) + "|" + Encode(folderName) + "|" + Encode(launchOptions) + "|" + Encode(iconName);
                g_done = true;
            }
            ImGui::SameLine();
            if (LauncherButton("Delete", ImVec2(buttonWidth, 36))) g_showDeleteConfirmation = true;
        }
        ImGui::End();
        if (mode == "progress" && promptVisible)
        {
            ImGui::OpenPopup("Steam Authentication Required");
            promptVisible = false;
        }
        ImGui::SetNextWindowPos(ImGui::GetMainViewport()->GetCenter(), ImGuiCond_Appearing, ImVec2(0.5f, 0.5f));
        if (ImGui::BeginPopupModal("Steam Authentication Required", nullptr, ImGuiWindowFlags_AlwaysAutoResize | ImGuiWindowFlags_NoSavedSettings))
        {
            if (promptDismissRequested)
            {
                promptDismissRequested = false;
                authInput[0] = '\0';
                ImGui::CloseCurrentPopup();
            }
            ImGui::PushTextWrapPos(410.0f);
            ImGui::TextWrapped("%s", promptText.c_str());
            ImGui::PopTextWrapPos();
            ImGui::SetNextItemWidth(390.0f);
            if (ImGui::IsWindowAppearing()) ImGui::SetKeyboardFocusHere();
            ImGuiInputTextFlags authFlags = ImGuiInputTextFlags_EnterReturnsTrue | (promptSecret ? ImGuiInputTextFlags_Password : ImGuiInputTextFlags_None);
            bool submit = ImGui::InputText("##authentication", authInput, sizeof(authInput), authFlags);
            if (LauncherButton("Submit", ImVec2(120, 34))) submit = true;
            ImGui::SameLine();
            if (promptAllowWaiting && LauncherButton("Continue Waiting", ImVec2(150, 34)))
            {
                WriteResult("SKIP|" + promptId);
                authInput[0] = '\0';
                progressStatus = "Waiting for Steam mobile approval...";
                ImGui::CloseCurrentPopup();
            }
            else if (!promptAllowWaiting && LauncherButton("Cancel Install", ImVec2(140, 34)))
            {
                cancelConfirmationRequested = true;
                ImGui::CloseCurrentPopup();
            }
            if (submit && authInput[0] != '\0')
            {
                WriteResult("INPUT|" + promptId + "|" + Encode(authInput));
                authInput[0] = '\0';
                progressStatus = "Authentication input submitted. Waiting for Steam...";
                ImGui::CloseCurrentPopup();
            }
            ImGui::EndPopup();
        }
        if (mode == "progress" && cancelConfirmationRequested)
        {
            ImGui::OpenPopup("Cancel Install?");
            cancelConfirmationRequested = false;
        }
        ImGui::SetNextWindowPos(ImGui::GetMainViewport()->GetCenter(), ImGuiCond_Appearing, ImVec2(0.5f, 0.5f));
        if (ImGui::BeginPopupModal("Cancel Install?", nullptr, ImGuiWindowFlags_AlwaysAutoResize | ImGuiWindowFlags_NoSavedSettings))
        {
            ImGui::TextUnformatted("Cancel this install?");
            ImGui::TextUnformatted("The partially downloaded version files will be deleted.");
            ImGui::Dummy(ImVec2(0, 6));
            if (LauncherButton("Cancel Install", ImVec2(140, 34)))
            {
                cancelRequested = true;
                promptDismissRequested = true;
                progressStatus = "Cancelling install and removing partial files...";
                WriteResult("CANCEL");
                ImGui::CloseCurrentPopup();
            }
            ImGui::SameLine();
            if (LauncherButton("Keep Downloading", ImVec2(150, 34))) ImGui::CloseCurrentPopup();
            ImGui::EndPopup();
        }
        if (g_showDeleteConfirmation)
        {
            // This is a normal ImGui window, so it can be moved without disturbing the editor.
            ImGui::SetNextWindowPos(ImGui::GetMainViewport()->GetCenter(), ImGuiCond_Appearing, ImVec2(0.5f, 0.5f));
            ImGui::Begin("Choose what to remove", &g_showDeleteConfirmation, ImGuiWindowFlags_AlwaysAutoResize | ImGuiWindowFlags_NoCollapse | ImGuiWindowFlags_NoResize | ImGuiWindowFlags_NoSavedSettings);
            ImGui::Dummy(ImVec2(0, 8));
            if (LauncherButton("Remove From Launcher", ImVec2(190, 32))) { result = "DELETE|launcher"; g_done = true; }
            if (LauncherButton("Delete Game Files", ImVec2(190, 32))) { result = "DELETE|game"; g_done = true; }
            if (LauncherButton("Back", ImVec2(190, 32))) g_showDeleteConfirmation = false;
            ImGui::End();
        }
        if (ImGui::BeginPopupModal("No Steam Launch Options", nullptr, ImGuiWindowFlags_AlwaysAutoResize))
        {
            ImGui::TextUnformatted("No Steam launch options were found for Subnautica.");
            if (LauncherButton("OK", ImVec2(90, 30))) ImGui::CloseCurrentPopup();
            ImGui::EndPopup();
        }
        if (mode == "mods" && showModRemovalConfirmation)
        {
            ImGui::OpenPopup("Confirm Mod Removal");
            showModRemovalConfirmation = false;
        }
        ImGui::SetNextWindowPos(ImGui::GetMainViewport()->GetCenter(), ImGuiCond_Appearing, ImVec2(0.5f, 0.5f));
        if (mode == "mods" && ImGui::BeginPopupModal("Confirm Mod Removal", nullptr, ImGuiWindowFlags_AlwaysAutoResize | ImGuiWindowFlags_NoSavedSettings))
        {
            ImGui::TextWrapped("Remove %s?", pendingModName.c_str());
            ImGui::TextUnformatted("This permanently removes its managed files from the selected game version.");
            ImGui::Dummy(ImVec2(0, 8));
            if (LauncherButton("Remove", ImVec2(110, 34)))
            {
                std::string command = pendingModCommand;
                if (!pendingModId.empty()) command += "|" + Encode(pendingModId);
                modBusy = true;
                modOperationError.clear();
                WriteResult(command);
                ImGui::CloseCurrentPopup();
            }
            ImGui::SameLine();
            if (LauncherButton("Cancel", ImVec2(110, 34))) ImGui::CloseCurrentPopup();
            ImGui::EndPopup();
        }
        if (showDetectionConfirmation)
        {
            ImGui::OpenPopup("Selected Version Detected");
            showDetectionConfirmation = false;
        }
        ImGui::SetNextWindowPos(ImGui::GetMainViewport()->GetCenter(), ImGuiCond_Appearing, ImVec2(0.5f, 0.5f));
        if (ImGui::BeginPopupModal("Selected Version Detected", nullptr, ImGuiWindowFlags_AlwaysAutoResize))
        {
            ImGui::TextWrapped("%s", detected.c_str());
            ImGui::Dummy(ImVec2(0, 8));
            ImGui::TextUnformatted("Is this correct?");
            if (LauncherButton("Yes", ImVec2(90, 30))) ImGui::CloseCurrentPopup();
            ImGui::SameLine();
            if (LauncherButton("No", ImVec2(90, 30))) { result = "CHOICE|existing"; g_done = true; ImGui::CloseCurrentPopup(); }
            ImGui::SameLine();
            if (LauncherButton("No and Fix", ImVec2(110, 30))) { result = "CHOICE|existing"; g_done = true; ImGui::CloseCurrentPopup(); }
            ImGui::EndPopup();
        }
        ImGui::Render();
        const float clear[] = { 0.035f, 0.055f, 0.095f, 1.0f };
        g_context->OMSetRenderTargets(1, &g_target, nullptr);
        g_context->ClearRenderTargetView(g_target, clear);
        ImGui_ImplDX11_RenderDrawData(ImGui::GetDrawData());
        g_swapChain->Present(1, 0);
    }
    WriteResult(result);
    ImGui_ImplDX11_Shutdown(); ImGui_ImplWin32_Shutdown(); ImGui::DestroyContext();
    CleanupDevice(); DestroyWindow(window); UnregisterClassW(windowClass.lpszClassName, instance); CoUninitialize();
    return 0;
}
