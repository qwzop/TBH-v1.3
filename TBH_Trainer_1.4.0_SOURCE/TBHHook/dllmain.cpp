// TBHHook.dll - injected into TaskBarHero.exe (Il2Cpp).
// Phase 1: game speed control via UnityEngine.Time::set_timeScale.
// Phase 2: Item spawn via ud.Stash.jbg + StashCache construction.
// Communicates with the C# trainer through a named shared-memory block.

#include <windows.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cstdarg>

// GameAssembly.dll RVAs — 1.00.08 defaults; trainer overwrites via shared memory for 1.00.09+
static constexpr uintptr_t kDefaultRvaStashJbg          = 0x8F0320;
static constexpr uintptr_t kDefaultRvaStashJbp          = 0x8F15D0;
static constexpr uintptr_t kDefaultRvaStashJbb          = 0x8EF8C0;
static constexpr uintptr_t kDefaultRvaStashSaveDataCtor = 0x99C9F0;
static constexpr int32_t   kStashMaxSlots        = 49;       // ud.Stash.becx

// ---- Shared state: MUST match TrainerBridge.cs byte-for-byte (56 bytes, pack=1) ----
#pragma pack(push, 1)
struct SharedState
{
    int32_t magic;         // 0x00  'TBH1' = 0x31484254
    int32_t heartbeat;     // 0x04
    int32_t speedEnabled;  // 0x08
    float   timeScale;     // 0x0C
    int32_t applyHp;       // 0x10
    float   hpValue;       // 0x14
    int32_t applyAtk;      // 0x18
    float   atkValue;      // 0x1C
    int32_t status;        // 0x20  bit0=il2cpp, bit1=Time, bit2=Stash
    int32_t spawnRequest;  // 0x24
    int32_t spawnItemKey;  // 0x28
    int32_t spawnGrade;    // 0x2C
    int32_t spawnResult;   // 0x30  1=ok -1=fail -2=bad key -3=create fail -4=stash full
    int32_t spawnDetail;   // 0x34  last failing step (debug)
    int32_t buildId;       // 0x38  10008 / 10009
    int32_t rvaStashJbg;   // 0x3C  from trainer (0 = use default)
    int32_t rvaStashJbp;   // 0x40
    int32_t rvaStashJbb;   // 0x44
    int32_t rvaSaveCtor;   // 0x48
    int32_t rvaAddItem;    // 0x4C  ue.ti.blk (1.00.09+)
};
#pragma pack(pop)

static_assert(sizeof(SharedState) == 80, "SharedState size mismatch");

static const wchar_t* kMapName = L"TBHTrainerShared";
static const int32_t  kMagic   = 0x31484254;
static const int      kMapSize = 80;

// Item scan report (trainer ItemScanBridge.cs)
#pragma pack(push, 1)
struct ScanState
{
    int32_t magic;    // 0x53424854 'TBHS'
    int32_t request;  // odd=catalog dump, even=stash scan
    int32_t done;     // 0=pending 1=ok -1=fail 2=truncated
    int32_t length;
    char    text[524288 - 16];
};
#pragma pack(pop)
static_assert(sizeof(ScanState) == 524288, "ScanState size mismatch");

// Dedicated hero scan/control state.
#pragma pack(push, 1)
struct HeroScanState
{
    int32_t magic;
    int32_t request;
    int32_t done;
    int32_t length;
    int32_t command;   // 0=scan, 1=current HP, 2=max HP, 3=attack damage, 4=attack speed, 5=both HP
    int32_t heroIndex;
    float   value;
    int32_t reserved;
    char    text[524288 - 32];
};
#pragma pack(pop)
static_assert(sizeof(HeroScanState) == 524288, "HeroScanState size mismatch");

static const wchar_t* kScanMapName = L"TBHTrainerScan";
static const int32_t  kScanMagic   = 0x53424854;
static const int      kScanMapSize = 524288;

static ScanState* g_scan           = nullptr;
static int32_t    g_lastScanReq    = 0;
static const wchar_t* kHeroScanMapName = L"TBHTrainerHeroes";
static const int32_t  kHeroScanMagic   = 0x48524854;
static HeroScanState* g_heroScan       = nullptr;
static int32_t    g_lastHeroScanReq    = 0;
static volatile LONG g_spawnBusy   = 0;
static int32_t    g_remapInKey     = 0;
static int32_t    g_remapInGrade   = -1;
static int32_t    g_remapOutKey    = 0;

// ---- il2cpp exports ----
typedef void* (*il2cpp_domain_get_t)();
typedef void* (*il2cpp_thread_attach_t)(void* domain);
typedef void** (*il2cpp_domain_get_assemblies_t)(void* domain, size_t* size);
typedef void* (*il2cpp_assembly_get_image_t)(void* assembly);
typedef void* (*il2cpp_class_from_name_t)(void* image, const char* ns, const char* name);
typedef void* (*il2cpp_class_get_method_from_name_t)(void* klass, const char* name, int argsCount);
typedef void* (*il2cpp_runtime_invoke_t)(void* method, void* obj, void** params, void** exc);
typedef void* (*il2cpp_object_new_t)(void* klass);
typedef void* (*il2cpp_class_get_field_from_name_t)(void* klass, const char* name);
typedef void  (*il2cpp_field_get_value_t)(void* obj, void* field, void* value);
typedef void  (*il2cpp_field_set_value_t)(void* obj, void* field, void* value);
typedef void  (*il2cpp_field_static_get_value_t)(void* field, void* value);
typedef size_t (*il2cpp_image_get_class_count_t)(void* image);
typedef void* (*il2cpp_image_get_class_t)(void* image, size_t index);
typedef const char* (*il2cpp_class_get_name_t)(void* klass);
typedef const char* (*il2cpp_class_get_namespace_t)(void* klass);
typedef void* (*il2cpp_class_get_methods_t)(void* klass, void** iter);
typedef const char* (*il2cpp_method_get_name_t)(void* method);
typedef uint8_t (*il2cpp_method_get_param_count_t)(void* method);
typedef void* (*il2cpp_method_get_pointer_t)(void* method);
typedef void* (*il2cpp_class_get_fields_t)(void* klass, void** iter);
typedef const char* (*il2cpp_field_get_name_t)(void* field);
typedef int32_t (*il2cpp_field_get_flags_t)(void* field);
typedef void* (*il2cpp_method_get_param_t)(void* method, uint32_t index);
typedef void* (*il2cpp_class_from_type_t)(const void* type);
typedef void* (*il2cpp_object_get_class_t)(void* obj);
typedef void* (*il2cpp_class_get_parent_t)(void* klass);
typedef uint32_t (*il2cpp_array_length_t)(void* array);
typedef void* (*il2cpp_array_get_t)(void* array, size_t elementSize, size_t index);

static il2cpp_domain_get_t                 il2cpp_domain_get;
static il2cpp_thread_attach_t              il2cpp_thread_attach;
static il2cpp_domain_get_assemblies_t      il2cpp_domain_get_assemblies;
static il2cpp_assembly_get_image_t         il2cpp_assembly_get_image;
static il2cpp_class_from_name_t            il2cpp_class_from_name;
static il2cpp_class_get_method_from_name_t il2cpp_class_get_method_from_name;
static il2cpp_runtime_invoke_t             il2cpp_runtime_invoke;
static il2cpp_object_new_t                 il2cpp_object_new;
static il2cpp_class_get_field_from_name_t  il2cpp_class_get_field_from_name;
static il2cpp_field_get_value_t            il2cpp_field_get_value;
static il2cpp_field_set_value_t            il2cpp_field_set_value;
static il2cpp_field_static_get_value_t     il2cpp_field_static_get_value;
static il2cpp_image_get_class_count_t      il2cpp_image_get_class_count;
static il2cpp_image_get_class_t            il2cpp_image_get_class;
static il2cpp_class_get_name_t             il2cpp_class_get_name;
static il2cpp_class_get_namespace_t        il2cpp_class_get_namespace;
static il2cpp_class_get_methods_t          il2cpp_class_get_methods;
static il2cpp_method_get_name_t            il2cpp_method_get_name;
static il2cpp_method_get_param_count_t     il2cpp_method_get_param_count;
static il2cpp_method_get_pointer_t         il2cpp_method_get_pointer;
static il2cpp_class_get_fields_t           il2cpp_class_get_fields;
static il2cpp_field_get_name_t             il2cpp_field_get_name;
static il2cpp_field_get_flags_t            il2cpp_field_get_flags;
static il2cpp_method_get_param_t           il2cpp_method_get_param;
static il2cpp_class_from_type_t            il2cpp_class_from_type;
static il2cpp_object_get_class_t           il2cpp_object_get_class;
static il2cpp_class_get_parent_t           il2cpp_class_get_parent;

static SharedState* g_shared            = nullptr;
static void*        g_domain            = nullptr;
static int32_t      g_resolveCounter    = 0;
static HMODULE      g_gameAssembly      = nullptr;

static void*        g_timeClass         = nullptr;
static void*        g_setTimeScale      = nullptr;

static void*        g_stashClass        = nullptr;
static void*        g_stashCacheClass   = nullptr;
static void*        g_stashSaveClass    = nullptr;
static void*        g_stashInstance     = nullptr;
static void*        g_jbg               = nullptr;
static void*        g_jbp               = nullptr;
static void*        g_jbi               = nullptr;
static void*        g_tiClass           = nullptr;
static void*        g_blkMethod         = nullptr;
static void*        g_isrMethod         = nullptr;
static void*        g_hyyMethod         = nullptr;
static void*        g_isiMethod         = nullptr;
static void*        g_gesMethod         = nullptr;
static void*        g_iskMethod         = nullptr;
static void*        g_ispMethod         = nullptr;
static void*        g_ishMethod         = nullptr;
static void*        g_opdMethod         = nullptr;
static void*        g_iryMethod         = nullptr;
static void*        g_islMethod         = nullptr;
static void*        g_esxMethod         = nullptr;
static void*        g_ysClass           = nullptr;
static void*        g_ysInstance        = nullptr;
static void*        g_lqkMethod         = nullptr;
static void*        g_playerSaveClass   = nullptr;
static void*        g_itemSaveClass     = nullptr;
static void*        g_tfClass           = nullptr;
static void*        g_itemInfoClass     = nullptr;
static il2cpp_array_length_t   il2cpp_array_length;
static il2cpp_array_get_t      il2cpp_array_get;
static void*        g_jboMethod         = nullptr;
static void*        g_jcdMethod         = nullptr;
static void*        g_yqClass            = nullptr;
static void*        g_yqInstance        = nullptr;
static void*        g_looMethod         = nullptr;
static void*        g_lopMethod         = nullptr;
static void*        g_addItemResultClass = nullptr;
static int32_t      g_lastSpawnDetail   = 0;
static void*        g_stashSaveCtor     = nullptr;
static int32_t      g_lastSpawnReq      = 0;
static uint64_t     g_nextUniqueId      = 0xDEAD000000000001ULL;
static uintptr_t    g_rvaStashJbg       = kDefaultRvaStashJbg;
static uintptr_t    g_rvaStashJbp       = kDefaultRvaStashJbp;
static uintptr_t    g_rvaStashJbb       = kDefaultRvaStashJbb;
static uintptr_t    g_rvaSaveCtor       = kDefaultRvaStashSaveDataCtor;
static uintptr_t    g_rvaAddItem        = 0;
static int32_t      g_buildId           = 0;

// Native fallbacks (il2cpp x64: RCX=this, then args)
typedef void* (__fastcall *NativeJbp_t)(void* self, int32_t index);
typedef void  (__fastcall *NativeJbg_t)(void* self, uint64_t uid, void* cache);
typedef void  (__fastcall *NativeJbb_t)(void* self);

static NativeJbp_t g_nativeJbp = nullptr;
static NativeJbg_t g_nativeJbg = nullptr;
static NativeJbb_t g_nativeJbb = nullptr;
static void* g_obscuredFloatEncryptMethod = nullptr;

struct HeroLocks
{
    bool currentHp;
    bool maxHp;
    bool attackDamage;
    bool attackSpeed;
    float currentHpValue;
    float maxHpValue;
    float attackDamageValue;
    float attackSpeedValue;
};
static HeroLocks g_heroLocks[3] = {};

// -------------------------------------------------------------------------

static void* ResolveStashInstance();
static void* ResolveYqInstance();
static void* TryStaticField(void* klass, const char* name);
static int32_t ListSize(void* list);
static void* ReadListItem(void* list, int32_t index);
static size_t ScanAppend(char* buf, size_t cap, size_t pos, const char* fmt, ...);
static void RunHeroScan();
static const char* GradeTypeName(int32_t grade);
static int32_t ReadGradeFromItemKey(int32_t itemKey);
static int32_t ReadGradeFromInfo(void* info);
static int32_t GetCatalogGradeForKey(int32_t itemKey);
static bool ItemKeyExists(int32_t itemKey);
static void* CreateItemInstance(void* itemInfo, uint64_t uid);

template <typename T>
static T Resolve(HMODULE m, const char* name)
{
    return reinterpret_cast<T>(GetProcAddress(m, name));
}

static uintptr_t GameAssemblyBase()
{
    if (!g_gameAssembly) return 0;
    return reinterpret_cast<uintptr_t>(g_gameAssembly);
}

static void* RvaToPtr(uintptr_t rva)
{
    uintptr_t base = GameAssemblyBase();
    return base ? reinterpret_cast<void*>(base + rva) : nullptr;
}

static bool LoadIl2Cpp()
{
    g_gameAssembly = GetModuleHandleW(L"GameAssembly.dll");
    if (!g_gameAssembly) return false;

    il2cpp_domain_get                 = Resolve<il2cpp_domain_get_t>(g_gameAssembly,                 "il2cpp_domain_get");
    il2cpp_thread_attach              = Resolve<il2cpp_thread_attach_t>(g_gameAssembly,              "il2cpp_thread_attach");
    il2cpp_domain_get_assemblies      = Resolve<il2cpp_domain_get_assemblies_t>(g_gameAssembly,      "il2cpp_domain_get_assemblies");
    il2cpp_assembly_get_image         = Resolve<il2cpp_assembly_get_image_t>(g_gameAssembly,         "il2cpp_assembly_get_image");
    il2cpp_class_from_name            = Resolve<il2cpp_class_from_name_t>(g_gameAssembly,            "il2cpp_class_from_name");
    il2cpp_class_get_method_from_name = Resolve<il2cpp_class_get_method_from_name_t>(g_gameAssembly, "il2cpp_class_get_method_from_name");
    il2cpp_runtime_invoke             = Resolve<il2cpp_runtime_invoke_t>(g_gameAssembly,             "il2cpp_runtime_invoke");
    il2cpp_object_new                 = Resolve<il2cpp_object_new_t>(g_gameAssembly,                 "il2cpp_object_new");
    il2cpp_class_get_field_from_name  = Resolve<il2cpp_class_get_field_from_name_t>(g_gameAssembly,  "il2cpp_class_get_field_from_name");
    il2cpp_field_get_value            = Resolve<il2cpp_field_get_value_t>(g_gameAssembly,            "il2cpp_field_get_value");
    il2cpp_field_set_value            = Resolve<il2cpp_field_set_value_t>(g_gameAssembly,            "il2cpp_field_set_value");
    il2cpp_field_static_get_value     = Resolve<il2cpp_field_static_get_value_t>(g_gameAssembly,     "il2cpp_field_static_get_value");
    il2cpp_image_get_class_count      = Resolve<il2cpp_image_get_class_count_t>(g_gameAssembly,      "il2cpp_image_get_class_count");
    il2cpp_image_get_class            = Resolve<il2cpp_image_get_class_t>(g_gameAssembly,            "il2cpp_image_get_class");
    il2cpp_class_get_name             = Resolve<il2cpp_class_get_name_t>(g_gameAssembly,             "il2cpp_class_get_name");
    il2cpp_class_get_namespace        = Resolve<il2cpp_class_get_namespace_t>(g_gameAssembly,        "il2cpp_class_get_namespace");
    il2cpp_class_get_methods          = Resolve<il2cpp_class_get_methods_t>(g_gameAssembly,          "il2cpp_class_get_methods");
    il2cpp_method_get_name            = Resolve<il2cpp_method_get_name_t>(g_gameAssembly,            "il2cpp_method_get_name");
    il2cpp_method_get_param_count     = Resolve<il2cpp_method_get_param_count_t>(g_gameAssembly,     "il2cpp_method_get_param_count");
    il2cpp_method_get_pointer         = Resolve<il2cpp_method_get_pointer_t>(g_gameAssembly,         "il2cpp_method_get_pointer");
    il2cpp_class_get_fields           = Resolve<il2cpp_class_get_fields_t>(g_gameAssembly,           "il2cpp_class_get_fields");
    il2cpp_field_get_name             = Resolve<il2cpp_field_get_name_t>(g_gameAssembly,             "il2cpp_field_get_name");
    il2cpp_field_get_flags            = Resolve<il2cpp_field_get_flags_t>(g_gameAssembly,            "il2cpp_field_get_flags");
    il2cpp_method_get_param           = Resolve<il2cpp_method_get_param_t>(g_gameAssembly,           "il2cpp_method_get_param");
    il2cpp_class_from_type            = Resolve<il2cpp_class_from_type_t>(g_gameAssembly,            "il2cpp_class_from_type");
    il2cpp_object_get_class           = Resolve<il2cpp_object_get_class_t>(g_gameAssembly,           "il2cpp_object_get_class");
    il2cpp_class_get_parent           = Resolve<il2cpp_class_get_parent_t>(g_gameAssembly,           "il2cpp_class_get_parent");
    il2cpp_array_length               = Resolve<il2cpp_array_length_t>(g_gameAssembly,               "il2cpp_array_length");
    il2cpp_array_get                  = Resolve<il2cpp_array_get_t>(g_gameAssembly,                   "il2cpp_array_get");

    return il2cpp_domain_get && il2cpp_thread_attach && il2cpp_domain_get_assemblies &&
           il2cpp_assembly_get_image && il2cpp_class_from_name &&
           il2cpp_class_get_method_from_name && il2cpp_runtime_invoke &&
           il2cpp_object_new;
}

static void* FindClass(void* domain, const char* ns, const char* name)
{
    size_t count = 0;
    void** assemblies = il2cpp_domain_get_assemblies(domain, &count);
    for (size_t i = 0; i < count; ++i)
    {
        void* img = il2cpp_assembly_get_image(assemblies[i]);
        if (!img) continue;
        void* klass = il2cpp_class_from_name(img, ns, name);
        if (klass) return klass;
    }
    return nullptr;
}

static bool Streq(const char* a, const char* b)
{
    if (!a || !b) return false;
    while (*a && *b) { if (*a++ != *b++) return false; }
    return *a == *b;
}

static bool StrContains(const char* hay, const char* needle)
{
    if (!hay || !needle || !*needle) return false;
    for (const char* h = hay; *h; ++h)
    {
        const char* n = needle;
        const char* p = h;
        while (*n && *p && *n == *p) { ++n; ++p; }
        if (!*n) return true;
    }
    return false;
}

static void* FindClassByNameContains(void* domain, const char* needle)
{
    if (!il2cpp_image_get_class_count || !il2cpp_image_get_class || !il2cpp_class_get_name)
        return nullptr;

    size_t asmCount = 0;
    void** assemblies = il2cpp_domain_get_assemblies(domain, &asmCount);
    for (size_t a = 0; a < asmCount; ++a)
    {
        void* img = il2cpp_assembly_get_image(assemblies[a]);
        if (!img) continue;
        size_t n = il2cpp_image_get_class_count(img);
        for (size_t i = 0; i < n; ++i)
        {
            void* klass = il2cpp_image_get_class(img, i);
            if (!klass) continue;
            const char* cn = il2cpp_class_get_name(klass);
            if (cn && StrContains(cn, needle)) return klass;
        }
    }
    return nullptr;
}

static void* FindClassWithMethodRva(void* domain, uintptr_t rva, const char* optName, int argCount)
{
    void* target = RvaToPtr(rva);
    if (!target || !il2cpp_image_get_class_count || !il2cpp_class_get_methods)
        return nullptr;

    size_t asmCount = 0;
    void** assemblies = il2cpp_domain_get_assemblies(domain, &asmCount);
    for (size_t a = 0; a < asmCount; ++a)
    {
        void* img = il2cpp_assembly_get_image(assemblies[a]);
        if (!img) continue;
        size_t n = il2cpp_image_get_class_count(img);
        for (size_t i = 0; i < n; ++i)
        {
            void* klass = il2cpp_image_get_class(img, i);
            if (!klass) continue;

            void* iter = nullptr;
            void* method = nullptr;
            while ((method = il2cpp_class_get_methods(klass, &iter)) != nullptr)
            {
                if (optName && il2cpp_method_get_name)
                {
                    const char* mn = il2cpp_method_get_name(method);
                    if (!mn || !Streq(mn, optName)) continue;
                }
                if (il2cpp_method_get_param_count && il2cpp_method_get_param_count(method) != (uint8_t)argCount)
                    continue;

                if (il2cpp_method_get_pointer)
                {
                    void* ptr = il2cpp_method_get_pointer(method);
                    if (ptr == target) return klass;
                }
                else if (optName)
                {
                    return klass;
                }
            }
        }
    }
    return nullptr;
}

static void* FindMethodOnClass(void* klass, const char* name, int argCount)
{
    if (!klass) return nullptr;
    void* m = il2cpp_class_get_method_from_name(klass, name, argCount);
    if (m) return m;

    if (!il2cpp_class_get_methods || !il2cpp_method_get_name) return nullptr;
    void* iter = nullptr;
    void* method = nullptr;
    while ((method = il2cpp_class_get_methods(klass, &iter)) != nullptr)
    {
        const char* mn = il2cpp_method_get_name(method);
        if (!mn || !Streq(mn, name)) continue;
        if (il2cpp_method_get_param_count && il2cpp_method_get_param_count(method) != (uint8_t)argCount)
            continue;
        return method;
    }
    return nullptr;
}

static void* FindFieldOnClassOrParents(void* klass, const char* name)
{
    if (!klass || !name || !il2cpp_class_get_field_from_name) return nullptr;
    for (void* k = klass; k; k = il2cpp_class_get_parent ? il2cpp_class_get_parent(k) : nullptr)
    {
        void* field = il2cpp_class_get_field_from_name(k, name);
        if (field) return field;
        if (!il2cpp_class_get_parent) break;
    }
    return nullptr;
}

static void* ClassFromMethodParam(void* method, uint32_t index)
{
    if (!method || !il2cpp_method_get_param || !il2cpp_class_from_type) return nullptr;
    const void* type = il2cpp_method_get_param(method, index);
    if (!type) return nullptr;
    return il2cpp_class_from_type(type);
}

static void SyncStashRvasFromShared()
{
    if (!g_shared) return;
    auto pick = [](int32_t fromTrainer, uintptr_t fallback) -> uintptr_t
    {
        return fromTrainer > 0 ? static_cast<uintptr_t>(fromTrainer) : fallback;
    };
    g_buildId     = g_shared->buildId;
    g_rvaStashJbg = pick(g_shared->rvaStashJbg, kDefaultRvaStashJbg);
    g_rvaStashJbp = pick(g_shared->rvaStashJbp, kDefaultRvaStashJbp);
    g_rvaStashJbb = pick(g_shared->rvaStashJbb, kDefaultRvaStashJbb);
    g_rvaSaveCtor = pick(g_shared->rvaSaveCtor, kDefaultRvaStashSaveDataCtor);
    g_rvaAddItem  = pick(g_shared->rvaAddItem, 0);
}

static void InitNativeStashCalls()
{
    SyncStashRvasFromShared();
    g_nativeJbg = reinterpret_cast<NativeJbg_t>(RvaToPtr(g_rvaStashJbg));
    g_nativeJbp = reinterpret_cast<NativeJbp_t>(RvaToPtr(g_rvaStashJbp));
    g_nativeJbb = reinterpret_cast<NativeJbb_t>(RvaToPtr(g_rvaStashJbb));
}

static int32_t BuildStatusFlags()
{
    int32_t flags = 1;
    if (g_setTimeScale) flags |= 2;
    if ((g_jbg || g_nativeJbg) && g_isrMethod && g_isiMethod)
        flags |= 4;
    else if (g_blkMethod || (g_stashCacheClass && (g_jbg || g_nativeJbg)))
        flags |= 4;
    return flags;
}

static void* FindMethodAny(void* klass, const char* const* names, const int* argCounts, int n)
{
    if (!klass) return nullptr;
    for (int i = 0; i < n; ++i)
    {
        void* m = FindMethodOnClass(klass, names[i], argCounts[i]);
        if (m) return m;
    }
    return nullptr;
}

static bool ResolveItemAdd()
{
    if (g_blkMethod && g_isrMethod) return true;
    if (!g_domain) return false;

    if (!g_tiClass)
    {
        g_tiClass = FindClass(g_domain, "", "ti");
        if (!g_tiClass) g_tiClass = FindClass(g_domain, "ue", "ti");
        if (!g_tiClass) g_tiClass = FindClass(g_domain, "ud", "ti");
    }
    if (g_tiClass)
    {
        if (!g_blkMethod) g_blkMethod = FindMethodOnClass(g_tiClass, "blk", 3);
        if (!g_isrMethod) g_isrMethod = FindMethodOnClass(g_tiClass, "isr", 1);
        if (!g_hyyMethod) g_hyyMethod = FindMethodOnClass(g_tiClass, "hyy", 1);
        if (!g_isiMethod) g_isiMethod = FindMethodOnClass(g_tiClass, "isi", 3);
        if (!g_gesMethod) g_gesMethod = FindMethodOnClass(g_tiClass, "ges", 3);
        if (!g_iskMethod) g_iskMethod = FindMethodOnClass(g_tiClass, "isk", 3);
        if (!g_ishMethod) g_ishMethod = FindMethodOnClass(g_tiClass, "ish", 1);
        if (!g_opdMethod) g_opdMethod = FindMethodOnClass(g_tiClass, "opd", 1);
        if (!g_islMethod) g_islMethod = FindMethodOnClass(g_tiClass, "isl", 1);
        if (!g_esxMethod) g_esxMethod = FindMethodOnClass(g_tiClass, "esx", 1);
        if (!g_iryMethod) g_iryMethod = FindMethodOnClass(g_tiClass, "iry", 0);
    }
    if (!g_blkMethod && g_rvaAddItem > 0)
    {
        void* klass = FindClassWithMethodRva(g_domain, g_rvaAddItem, "blk", 3);
        if (klass)
        {
            g_tiClass = klass;
            g_blkMethod = FindMethodOnClass(klass, "blk", 3);
        }
    }
    return g_blkMethod != nullptr || (g_isrMethod != nullptr && g_isiMethod != nullptr);
}

static void UpdateSharedStatus()
{
    if (g_shared) g_shared->status = BuildStatusFlags();
}

static bool TryResolveStash()
{
    if (!g_domain) return false;

    InitNativeStashCalls();

    // 1) Direct name lookup — 1.00.08 ud.* / 1.00.09 ue.*
    if (!g_stashClass)
    {
        static const char* kNs[] = { "ue", "ud", "", "UD", nullptr };
        for (int i = 0; kNs[i]; ++i)
        {
            g_stashClass = FindClass(g_domain, kNs[i], "Stash");
            if (g_stashClass) break;
        }
    }
    if (!g_stashCacheClass)
    {
        static const char* kNs[] = { "ue", "ud", "", nullptr };
        for (int i = 0; kNs[i]; ++i)
        {
            g_stashCacheClass = FindClass(g_domain, kNs[i], "StashCache");
            if (g_stashCacheClass) break;
        }
        if (!g_stashCacheClass)
            g_stashCacheClass = FindClassByNameContains(g_domain, "StashCache");
    }
    if (!g_stashSaveClass)
    {
        static const char* kSaveNs[] = { "", "TaskbarHero.EasySaveData", "ud", "ue", nullptr };
        for (int i = 0; kSaveNs[i]; ++i)
        {
            g_stashSaveClass = FindClass(g_domain, kSaveNs[i], "StashSaveData");
            if (g_stashSaveClass) break;
        }
        if (!g_stashSaveClass) g_stashSaveClass = FindClassByNameContains(g_domain, "StashSaveData");
    }

    // 2) Locate Stash by native RVA of insert method (jbg 1.00.08 / jbn 1.00.09)
    if (!g_stashClass)
        g_stashClass = FindClassWithMethodRva(g_domain, g_rvaStashJbg, "jbn", 2);
    if (!g_stashClass)
        g_stashClass = FindClassWithMethodRva(g_domain, g_rvaStashJbg, "jbg", 2);
    if (!g_stashClass)
        g_stashClass = FindClassWithMethodRva(g_domain, g_rvaStashJbg, nullptr, 2);

    if (g_stashClass)
    {
        if (!g_jbg)
        {
            static const char* kIns[] = { "jbn", "jbg" };
            static const int kInsArgs[] = { 2, 2 };
            g_jbg = FindMethodAny(g_stashClass, kIns, kInsArgs, 2);
        }
        if (!g_jbp)
        {
            static const char* kSlot[] = { "jbw", "jbp" };
            static const int kSlotArgs[] = { 1, 1 };
            g_jbp = FindMethodAny(g_stashClass, kSlot, kSlotArgs, 2);
        }
        if (!g_jbi)
        {
            static const char* kInit[] = { "jbi", "jbb" };
            static const int kInitArgs[] = { 0, 0 };
            g_jbi = FindMethodAny(g_stashClass, kInit, kInitArgs, 2);
        }
        if (!g_jboMethod)
        {
            static const char* kAddTf[] = { "jbo", "jbp", "jbq" };
            static const int kAddTfArgs[] = { 2, 2, 2 };
            g_jboMethod = FindMethodAny(g_stashClass, kAddTf, kAddTfArgs, 3);
        }
        if (!g_jcdMethod) g_jcdMethod = FindMethodOnClass(g_stashClass, "jcd", 0);
    }

    // 3) StashCache type from jbg's 2nd parameter
    if (!g_stashCacheClass && g_jbg)
        g_stashCacheClass = ClassFromMethodParam(g_jbg, 1);

    if (!g_stashInstance)
        g_stashInstance = ResolveStashInstance();

    if (g_stashSaveClass && !g_stashSaveCtor)
        g_stashSaveCtor = il2cpp_class_get_method_from_name(g_stashSaveClass, ".ctor", 1);

    // 4) Stash singleton — scan every static field on Stash
    if (!g_stashInstance && g_stashClass && il2cpp_class_get_fields && il2cpp_field_static_get_value)
    {
        void* iter = nullptr;
        void* field = nullptr;
        while ((field = il2cpp_class_get_fields(g_stashClass, &iter)) != nullptr)
        {
            if (il2cpp_field_get_flags)
            {
                int32_t flags = il2cpp_field_get_flags(field);
                if ((flags & 0x0010) == 0) continue; // FIELD_ATTRIBUTE_STATIC
            }
            void* val = nullptr;
            il2cpp_field_static_get_value(field, &val);
            if (val) { g_stashInstance = val; break; }
        }
    }
    if (!g_stashInstance)
        g_stashInstance = ResolveStashInstance();

    // 5) Init stash tables (static call on 1.00.09 ue.Stash)
    if (g_jbi)
    {
        void* exc = nullptr;
        il2cpp_runtime_invoke(g_jbi, nullptr, nullptr, &exc);
    }
    else if (g_stashInstance && g_nativeJbb)
        g_nativeJbb(g_stashInstance);

    ResolveYqInstance();

    ResolveItemAdd();
    if (!g_ispMethod && g_tiClass)
        g_ispMethod = FindMethodOnClass(g_tiClass, "isp", 1);

    UpdateSharedStatus();
    return g_blkMethod != nullptr ||
           ((g_stashCacheClass != nullptr) && (g_jbg != nullptr || g_nativeJbg != nullptr));
}

static void SetTimeScale(float v)
{
    if (!g_setTimeScale) return;
    void* args[1] = { &v };
    void* exc = nullptr;
    il2cpp_runtime_invoke(g_setTimeScale, nullptr, args, &exc);
}

static uint64_t ReadSaveUniqueId(void* cacheObj)
{
    if (!cacheObj || !g_stashCacheClass || !g_stashSaveClass) return 0;

    void* save = nullptr;
    void* fSave = il2cpp_class_get_field_from_name(g_stashCacheClass, "beeg");
    if (!fSave) fSave = il2cpp_class_get_field_from_name(g_stashCacheClass, "saveData");
    if (fSave) il2cpp_field_get_value(cacheObj, fSave, &save);
    if (!save) return 0;

    uint64_t uid = 0;
    void* fUid = il2cpp_class_get_field_from_name(g_stashSaveClass, "ItemUniqueId");
    if (fUid) il2cpp_field_get_value(save, fUid, &uid);
    return uid;
}

static void* InvokeJbp(void* stashInst, int32_t index)
{
    (void)stashInst;
    if (g_jbp)
    {
        void* args[1] = { &index };
        void* exc = nullptr;
        return il2cpp_runtime_invoke(g_jbp, nullptr, args, &exc);
    }
    if (g_nativeJbp)
        return g_nativeJbp(nullptr, index);
    return nullptr;
}

static bool InvokeJbg(void* stashInst, uint64_t uid, void* cacheObj)
{
    (void)stashInst;
    if (g_jbg)
    {
        void* args[2] = { &uid, cacheObj };
        void* exc = nullptr;
        il2cpp_runtime_invoke(g_jbg, nullptr, args, &exc);
        if (exc == nullptr) return true;
    }
    if (g_nativeJbg)
    {
        g_nativeJbg(nullptr, uid, cacheObj);
        return true;
    }
    return false;
}

static void* TryStaticField(void* klass, const char* name)
{
    if (!klass || !il2cpp_field_static_get_value) return nullptr;
    void* field = il2cpp_class_get_field_from_name(klass, name);
    if (!field) return nullptr;
    void* value = nullptr;
    il2cpp_field_static_get_value(field, &value);
    return value;
}

static bool YqInstanceLooksValid(void* inst)
{
    if (!inst || !g_yqClass) return false;
    void* fDict = il2cpp_class_get_field_from_name(g_yqClass, "bfjb");
    void* fList = il2cpp_class_get_field_from_name(g_yqClass, "itemInfoData");
    if (fDict)
    {
        void* dict = nullptr;
        il2cpp_field_get_value(inst, fDict, &dict);
        if (dict) return true;
    }
    if (fList)
    {
        void* list = nullptr;
        il2cpp_field_get_value(inst, fList, &list);
        if (list && ListSize(list) > 0) return true;
    }
    return false;
}

static void* ResolveYqInstance()
{
    if (g_yqInstance && YqInstanceLooksValid(g_yqInstance)) return g_yqInstance;
    g_yqInstance = nullptr;
    if (!g_domain) return nullptr;

    if (!g_yqClass) g_yqClass = FindClass(g_domain, "", "yq");
    if (!g_yqClass) return nullptr;

    if (!g_looMethod) g_looMethod = FindMethodOnClass(g_yqClass, "loo", 1);
    if (!g_lopMethod) g_lopMethod = FindMethodOnClass(g_yqClass, "lop", 1);

    if (il2cpp_class_get_parent)
    {
        static const char* kInstFields[] = { "bbwm", "Instance", "instance", nullptr };
        for (void* k = g_yqClass; k; k = il2cpp_class_get_parent(k))
        {
            for (int fi = 0; kInstFields[fi]; ++fi)
            {
                void* cand = TryStaticField(k, kInstFields[fi]);
                if (YqInstanceLooksValid(cand))
                {
                    g_yqInstance = cand;
                    return cand;
                }
            }
        }
    }

    void* inst = TryStaticField(g_yqClass, "Instance");
    if (YqInstanceLooksValid(inst)) { g_yqInstance = inst; return inst; }

    if (il2cpp_domain_get_assemblies && il2cpp_class_get_fields && il2cpp_object_get_class)
    {
        size_t asmCount = 0;
        void** assemblies = il2cpp_domain_get_assemblies(g_domain, &asmCount);
        for (size_t a = 0; a < asmCount; ++a)
        {
            void* img = il2cpp_assembly_get_image(assemblies[a]);
            if (!img || !il2cpp_image_get_class_count) continue;
            size_t n = il2cpp_image_get_class_count(img);
            for (size_t i = 0; i < n; ++i)
            {
                void* klass = il2cpp_image_get_class(img, i);
                if (!klass) continue;
                void* iter = nullptr;
                void* field = nullptr;
                while ((field = il2cpp_class_get_fields(klass, &iter)) != nullptr)
                {
                    if (il2cpp_field_get_flags && (il2cpp_field_get_flags(field) & 0x0010) == 0)
                        continue;
                    void* obj = nullptr;
                    il2cpp_field_static_get_value(field, &obj);
                    if (!obj) continue;
                    if (il2cpp_object_get_class(obj) != g_yqClass) continue;
                    if (YqInstanceLooksValid(obj))
                    {
                        g_yqInstance = obj;
                        return obj;
                    }
                }
            }
        }
    }
    return nullptr;
}

static int32_t ReadItemKeyFromInfo(void* info)
{
    if (!info) return -1;
    return *reinterpret_cast<int32_t*>(reinterpret_cast<uint8_t*>(info) + 0x30);
}

static int32_t ReadGradeFromInfo(void* info)
{
    if (!info) return -1;
    return *reinterpret_cast<int32_t*>(reinterpret_cast<uint8_t*>(info) + 0x38);
}

static bool InvokeBoolMethod(void* obj, void* method)
{
    if (!obj || !method) return false;
    void* exc = nullptr;
    void* ret = il2cpp_runtime_invoke(method, obj, nullptr, &exc);
    return !exc && ret && *reinterpret_cast<bool*>(ret);
}

static void* InvokeMethod0(void* obj, void* method)
{
    if (!method) return nullptr;
    void* exc = nullptr;
    return il2cpp_runtime_invoke(method, obj, nullptr, &exc);
}

static int32_t UnboxInt32(void* boxed)
{
    if (!boxed) return -1;
    return *reinterpret_cast<int32_t*>(reinterpret_cast<uint8_t*>(boxed) + 0x10);
}

static size_t CatalogAppendFromList(void* yqInst, char* text, size_t cap, size_t pos, int32_t& count, bool& truncated)
{
    if (!yqInst || !g_yqClass) return pos;
    void* fList = il2cpp_class_get_field_from_name(g_yqClass, "itemInfoData");
    if (!fList) return pos;

    void* list = nullptr;
    il2cpp_field_get_value(yqInst, fList, &list);
    if (!list) return pos;

    int32_t size = ListSize(list);
    for (int32_t i = 0; i < size; ++i)
    {
        void* info = ReadListItem(list, i);
        if (!info) continue;
        int32_t key = ReadItemKeyFromInfo(info);
        if (key <= 0) continue;
        int32_t grade = ReadGradeFromInfo(info);
        size_t need = 64;
        if (pos + need >= cap) { truncated = true; return pos; }
        pos = ScanAppend(text, cap, pos, "ITEM %d grade=%d (%s)\r\n", key, grade, GradeTypeName(grade));
        count++;
    }
    return pos;
}

static size_t CatalogAppendFromDict(void* yqInst, char* text, size_t cap, size_t pos, int32_t& count, bool& truncated)
{
    if (!yqInst || !g_yqClass || !il2cpp_object_get_class) return pos;
    void* fDict = il2cpp_class_get_field_from_name(g_yqClass, "bfjb");
    if (!fDict) return pos;

    void* dict = nullptr;
    il2cpp_field_get_value(yqInst, fDict, &dict);
    if (!dict) return pos;

    void* dictClass = il2cpp_object_get_class(dict);
    void* getKeys = il2cpp_class_get_method_from_name(dictClass, "get_Keys", 0);
    if (!getKeys) return pos;

    void* keys = InvokeMethod0(dict, getKeys);
    if (!keys) return pos;

    void* keysClass = il2cpp_object_get_class(keys);
    void* getEnum = il2cpp_class_get_method_from_name(keysClass, "GetEnumerator", 0);
    if (!getEnum) return pos;

    void* enumerator = InvokeMethod0(keys, getEnum);
    if (!enumerator) return pos;

    void* enumClass = il2cpp_object_get_class(enumerator);
    void* moveNext = il2cpp_class_get_method_from_name(enumClass, "MoveNext", 0);
    void* getCurrent = il2cpp_class_get_method_from_name(enumClass, "get_Current", 0);
    if (!moveNext || !getCurrent) return pos;

    while (InvokeBoolMethod(enumerator, moveNext))
    {
        void* exc = nullptr;
        void* cur = il2cpp_runtime_invoke(getCurrent, enumerator, nullptr, &exc);
        if (exc || !cur) continue;
        int32_t key = UnboxInt32(cur);
        if (key <= 0) continue;

        int32_t grade = -1;
        if (g_yqInstance && g_looMethod)
        {
            void* args[1] = { &key };
            exc = nullptr;
            void* info = il2cpp_runtime_invoke(g_looMethod, g_yqInstance, args, &exc);
            if (!exc && info) grade = ReadGradeFromInfo(info);
        }
        if (grade < 0) grade = ReadGradeFromItemKey(key);

        if (pos + 64 >= cap) { truncated = true; return pos; }
        pos = ScanAppend(text, cap, pos, "ITEM %d grade=%d (%s)\r\n", key, grade, GradeTypeName(grade));
        count++;
    }
    return pos;
}

static void* ResolveStashInstance()
{
    if (!g_stashClass) return nullptr;

    static const char* kStaticFields[] = {
        "bedv", "bedw", "bedy", "becv", "becw", "becy", "becz",
        "Instance", "instance", "_instance", "s_instance"
    };
    for (const char* name : kStaticFields)
    {
        void* inst = TryStaticField(g_stashClass, name);
        if (inst) return inst;
    }

    // Some builds expose the live stash on a static manager type.
    void* domain = g_domain ? g_domain : il2cpp_domain_get();
    void* mgr = FindClass(domain, "ud", "StashManager");
    if (!mgr) mgr = FindClass(domain, "", "StashManager");
    if (mgr)
    {
        for (const char* name : kStaticFields)
        {
            void* inst = TryStaticField(mgr, name);
            if (inst) return inst;
        }
    }

    return nullptr;
}

static int32_t FindEmptySlot(void* stashInst)
{
    (void)stashInst;
    for (int32_t i = 0; i < kStashMaxSlots; ++i)
    {
        void* cache = InvokeJbp(nullptr, i);
        if (!cache) return i;
        if (ReadSaveUniqueId(cache) == 0) return i;
    }
    return -1;
}

static void SetFieldInt32(void* obj, void* klass, const char* name, int32_t v)
{
    void* f = il2cpp_class_get_field_from_name(klass, name);
    if (f) il2cpp_field_set_value(obj, f, &v);
}

static void SetFieldUInt64(void* obj, void* klass, const char* name, uint64_t v)
{
    void* f = il2cpp_class_get_field_from_name(klass, name);
    if (f) il2cpp_field_set_value(obj, f, &v);
}

static void SetFieldBool(void* obj, void* klass, const char* name, bool v)
{
    void* f = il2cpp_class_get_field_from_name(klass, name);
    if (f) il2cpp_field_set_value(obj, f, &v);
}

static int32_t ReadAddResultCode(void* resultPtr)
{
    if (!resultPtr) return -1;
    // AddItemResult is an Il2Cpp value-type; AddResult field @ 0x14 (see dump)
    int32_t direct = *reinterpret_cast<int32_t*>(reinterpret_cast<uint8_t*>(resultPtr) + 0x14);

    if (!g_addItemResultClass)
        g_addItemResultClass = FindClass(g_domain, "TaskbarHero", "AddItemResult");
    if (g_addItemResultClass)
    {
        int32_t viaField = -1;
        void* f = il2cpp_class_get_field_from_name(g_addItemResultClass, "AddResult");
        if (f) il2cpp_field_get_value(resultPtr, f, &viaField);
        if (viaField >= 0) return viaField;
    }
    return direct;
}

static bool AddItemResultOk(void* resultPtr)
{
    if (!resultPtr) return false;
    int32_t code = ReadAddResultCode(resultPtr);
    // TaskbarHero add-item APIs: 0/1 = success in 1.00.09 testing
    return code == 0 || code == 1;
}

static void SetSpawnFailFromResult(void* resultPtr)
{
    int32_t code = ReadAddResultCode(resultPtr);
    g_lastSpawnDetail = (code >= 0 && code <= 99) ? (-100 - code) : -6;
}

static void* ResolveItemInfo(int32_t itemKey)
{
    void* exc = nullptr;
    void* args[1] = { &itemKey };

    if (g_yqInstance && g_looMethod)
    {
        exc = nullptr;
        void* info = il2cpp_runtime_invoke(g_looMethod, g_yqInstance, args, &exc);
        if (!exc && info) return info;
    }

    if (g_isrMethod)
    {
        exc = nullptr;
        void* info = il2cpp_runtime_invoke(g_isrMethod, nullptr, args, &exc);
        if (!exc && info) return info;
    }
    if (g_hyyMethod)
    {
        exc = nullptr;
        void* info = il2cpp_runtime_invoke(g_hyyMethod, nullptr, args, &exc);
        if (!exc && info) return info;
    }
    return nullptr;
}

static int32_t GetCatalogGradeForKey(int32_t itemKey)
{
    void* info = ResolveItemInfo(itemKey);
    if (info) return ReadGradeFromInfo(info);
    return -1;
}

// Gear-style keys: 30XYYY — digit X (thousands) is EGradeType for many equipment rows.
static int32_t RemapItemKeyDigitGrade(int32_t itemKey, int32_t targetGrade)
{
    if (itemKey < 100000 || itemKey > 99999999) return itemKey;
    int32_t cur = (itemKey / 1000) % 10;
    if (cur == targetGrade) return itemKey;
    int32_t candidate = (itemKey / 10000) * 10000 + targetGrade * 1000 + (itemKey % 1000);
    if (ItemKeyExists(candidate)) return candidate;
    return itemKey;
}

static int32_t KeyGradeDigit(int32_t itemKey)
{
    if (itemKey < 100000) return -1;
    return (itemKey / 1000) % 10;
}

static int32_t TryCandidateKey(int32_t key, int32_t targetGrade, int32_t itemKey, int32_t* bestDist, int32_t* bestKey)
{
    if (key <= 0 || KeyGradeDigit(key) != targetGrade) return 0;
    if (!ItemKeyExists(key)) return 0;
    if (GetCatalogGradeForKey(key) != targetGrade) return 0;
    int32_t dist = key > itemKey ? key - itemKey : itemKey - key;
    if (dist < *bestDist)
    {
        *bestDist = dist;
        *bestKey = key;
        return 1;
    }
    return 0;
}

static int32_t FindItemKeyAlternateGrade(int32_t itemKey, int32_t targetGrade)
{
    if (itemKey < 100000) return itemKey;

    const int32_t prefix2 = itemKey / 10000;
    const int32_t suffix2 = itemKey % 100;
    const int32_t suffix3 = itemKey % 1000;
    int32_t bestKey = itemKey;
    int32_t bestDist = 0x7FFFFFFF;

    // Same 30x family, target grade digit only (304061 Immortal -> 308161 Divine)
    static const int32_t kSuffixDelta[] = { 0, 100, 200, 300, 1000, 1100, 1200, 1300 };
    for (int i = 0; i < 8; ++i)
    {
        int32_t key = prefix2 * 10000 + targetGrade * 1000 + suffix3 + kSuffixDelta[i];
        TryCandidateKey(key, targetGrade, itemKey, &bestDist, &bestKey);
        key = prefix2 * 10000 + targetGrade * 1000 + suffix2 + kSuffixDelta[i];
        TryCandidateKey(key, targetGrade, itemKey, &bestDist, &bestKey);
    }

    if (bestDist < 0x7FFFFFFF) return bestKey;
    return itemKey;
}

static int32_t ResolveSpawnItemKey(int32_t itemKey, int32_t requestedGrade, int32_t* outOriginalGrade)
{
    ResolveYqInstance();
    int32_t catalog = GetCatalogGradeForKey(itemKey);
    int32_t digit = KeyGradeDigit(itemKey);
    if (outOriginalGrade) *outOriginalGrade = (catalog >= 0 ? catalog : digit);

    if (requestedGrade < 0 || requestedGrade > 10) return itemKey;

    // Catalog ID already encodes grade in digit X of 30XYYY — do not remap 318192 etc.
    if (digit == requestedGrade) return itemKey;
    if (catalog == requestedGrade) return itemKey;

    if (itemKey == g_remapInKey && requestedGrade == g_remapInGrade && g_remapOutKey > 0)
        return g_remapOutKey;

    int32_t remapped = RemapItemKeyDigitGrade(itemKey, requestedGrade);
    if (remapped != itemKey && KeyGradeDigit(remapped) == requestedGrade &&
        GetCatalogGradeForKey(remapped) == requestedGrade)
    {
        g_remapInKey = itemKey;
        g_remapInGrade = requestedGrade;
        g_remapOutKey = remapped;
        return remapped;
    }

    remapped = FindItemKeyAlternateGrade(itemKey, requestedGrade);
    if (remapped != itemKey && KeyGradeDigit(remapped) == requestedGrade &&
        GetCatalogGradeForKey(remapped) == requestedGrade)
    {
        g_remapInKey = itemKey;
        g_remapInGrade = requestedGrade;
        g_remapOutKey = remapped;
        return remapped;
    }

    return itemKey;
}

static void* CreateItemInstanceWithGrade(void* itemInfo, uint64_t uid, int32_t grade)
{
    if (!itemInfo) return nullptr;
    if (grade >= 0 && grade <= 10)
        *reinterpret_cast<int32_t*>(reinterpret_cast<uint8_t*>(itemInfo) + 0x38) = grade;
    return CreateItemInstance(itemInfo, uid);
}

static void BumpUniqueIdSeed()
{
    if (g_nextUniqueId == 0xDEAD000000000001ULL)
        g_nextUniqueId = (GetTickCount64() << 16) | 0x10001ULL;
}

static bool ItemKeyExists(int32_t itemKey)
{
    if (ResolveItemInfo(itemKey)) return true;

    if (g_ispMethod)
    {
        void* args[1] = { &itemKey };
        void* exc = nullptr;
        void* sprite = il2cpp_runtime_invoke(g_ispMethod, nullptr, args, &exc);
        if (!exc && sprite) return true;
    }

    if (g_yqInstance && g_lopMethod)
    {
        void* args[1] = { &itemKey };
        void* exc = nullptr;
        void* ret = il2cpp_runtime_invoke(g_lopMethod, g_yqInstance, args, &exc);
        if (!exc && ret)
        {
            bool ok = *reinterpret_cast<bool*>(ret);
            if (ok) return true;
        }
    }
    return false;
}

static void* CreateItemInstance(void* itemInfo, uint64_t uid)
{
    if (!itemInfo) return nullptr;
    bool notify = true;
    void* args[3] = { itemInfo, &uid, &notify };
    void* exc = nullptr;

    if (g_isiMethod)
    {
        exc = nullptr;
        void* inst = il2cpp_runtime_invoke(g_isiMethod, nullptr, args, &exc);
        if (!exc && inst) return inst;
    }
    if (g_gesMethod)
    {
        exc = nullptr;
        void* inst = il2cpp_runtime_invoke(g_gesMethod, nullptr, args, &exc);
        if (!exc && inst) return inst;
    }
    return nullptr;
}

static int32_t FindEmptySlotIndex()
{
    if (g_jcdMethod)
    {
        void* exc = nullptr;
        void* ret = il2cpp_runtime_invoke(g_jcdMethod, nullptr, nullptr, &exc);
        if (!exc && ret)
        {
            int32_t slot = *reinterpret_cast<int32_t*>(ret);
            if (slot >= 0 && slot < kStashMaxSlots) return slot;
        }
    }
    return FindEmptySlot(nullptr);
}

static bool SpawnViaJbo(int32_t itemKey, int32_t grade)
{
    if (!g_jboMethod) return false;
    if (!ResolveItemAdd()) return false;

    void* itemInfo = ResolveItemInfo(itemKey);
    if (!itemInfo)
    {
        g_lastSpawnDetail = -2;
        return false;
    }

    uint64_t uid = g_nextUniqueId++;
    void* itemInst = CreateItemInstanceWithGrade(itemInfo, uid, grade);
    if (!itemInst)
    {
        g_lastSpawnDetail = -3;
        return false;
    }

    if (g_jbi)
    {
        void* exc = nullptr;
        il2cpp_runtime_invoke(g_jbi, nullptr, nullptr, &exc);
    }

    void* args[2] = { &uid, itemInst };
    void* exc = nullptr;
    il2cpp_runtime_invoke(g_jboMethod, nullptr, args, &exc);
    if (exc)
    {
        g_lastSpawnDetail = -5;
        return false;
    }
    return true;
}

static bool SpawnViaIsk(int32_t itemKey)
{
    if (!g_iskMethod) return false;
    if (!ResolveItemAdd()) return false;
    BumpUniqueIdSeed();

    uint64_t uid = g_nextUniqueId++;
    bool notify = true;
    void* args[3] = { &itemKey, &uid, &notify };
    void* exc = nullptr;
    void* ret = il2cpp_runtime_invoke(g_iskMethod, nullptr, args, &exc);
    if (exc)
    {
        g_lastSpawnDetail = -3;
        return false;
    }
    if (!AddItemResultOk(ret))
    {
        SetSpawnFailFromResult(ret);
        return false;
    }
    return true;
}

static bool SpawnViaBlk(int32_t itemKey)
{
    if (!ResolveItemAdd() || !g_blkMethod) return false;
    BumpUniqueIdSeed();

    uint64_t uid = g_nextUniqueId++;
    bool notify = true;
    void* args[3] = { &itemKey, &uid, &notify };
    void* exc = nullptr;
    void* ret = il2cpp_runtime_invoke(g_blkMethod, nullptr, args, &exc);
    if (exc)
    {
        g_lastSpawnDetail = -3;
        return false;
    }
    if (!AddItemResultOk(ret))
    {
        SetSpawnFailFromResult(ret);
        return false;
    }
    return true;
}

static bool SpawnViaAddApis(int32_t spawnKey)
{
    // Prefer blk (inventory) — one API only; do not chain on success
    if (g_blkMethod && SpawnViaBlk(spawnKey))
        return true;
    if (g_iskMethod && SpawnViaIsk(spawnKey))
        return true;
    return false;
}

static bool SpawnViaStash(int32_t itemKey, int32_t grade)
{
    if (!g_stashCacheClass || (!g_jbg && !g_nativeJbg)) return false;
    if (!ResolveItemAdd()) return false;

    void* itemInfo = ResolveItemInfo(itemKey);
    if (!itemInfo)
    {
        g_lastSpawnDetail = -2;
        return false;
    }

    uint64_t uid = g_nextUniqueId++;
    void* itemInst = CreateItemInstanceWithGrade(itemInfo, uid, grade);
    if (!itemInst)
    {
        g_lastSpawnDetail = -3;
        return false;
    }

    if (g_jboMethod)
    {
        void* argsJbo[2] = { &uid, itemInst };
        void* exc = nullptr;
        il2cpp_runtime_invoke(g_jboMethod, nullptr, argsJbo, &exc);
        if (!exc) return true;
        g_lastSpawnDetail = -5;
        return false;
    }

    if (g_jbi)
    {
        void* exc = nullptr;
        il2cpp_runtime_invoke(g_jbi, nullptr, nullptr, &exc);
    }

    int32_t slotIdx = FindEmptySlotIndex();
    if (slotIdx < 0)
    {
        g_lastSpawnDetail = -4;
        return false;
    }

    void* saveObj = nullptr;
    if (g_stashSaveClass && g_stashSaveCtor)
    {
        saveObj = il2cpp_object_new(g_stashSaveClass);
        if (saveObj)
        {
            void* ctorArgs[1] = { &slotIdx };
            void* exc = nullptr;
            il2cpp_runtime_invoke(g_stashSaveCtor, saveObj, ctorArgs, &exc);
            if (!exc)
            {
                SetFieldInt32(saveObj, g_stashSaveClass, "Index", slotIdx);
                SetFieldUInt64(saveObj, g_stashSaveClass, "ItemUniqueId", uid);
                SetFieldBool(saveObj, g_stashSaveClass, "IsUnLock", true);
            }
        }
    }
    if (!saveObj)
    {
        g_lastSpawnDetail = -3;
        return false;
    }

    void* cacheObj = il2cpp_object_new(g_stashCacheClass);
    if (!cacheObj)
    {
        g_lastSpawnDetail = -3;
        return false;
    }

    void* cacheCtor = il2cpp_class_get_method_from_name(g_stashCacheClass, ".ctor", 1);
    if (cacheCtor)
    {
        void* ctorArgs[1] = { saveObj };
        void* exc = nullptr;
        il2cpp_runtime_invoke(cacheCtor, cacheObj, ctorArgs, &exc);
        if (exc)
        {
            g_lastSpawnDetail = -5;
            return false;
        }
    }

    if (!InvokeJbg(nullptr, uid, cacheObj))
    {
        g_lastSpawnDetail = -5;
        return false;
    }
    return true;
}

static void EnsureItemMetaTypes()
{
    if (!g_tfClass)
        g_tfClass = FindClass(g_domain, "", "tf");
    if (!g_itemInfoClass)
    {
        g_itemInfoClass = FindClass(g_domain, "TaskbarHero.Data", "ItemInfoData");
        if (!g_itemInfoClass) g_itemInfoClass = FindClass(g_domain, "", "ItemInfoData");
    }
}

static int32_t ReadItemKeyFromTf(void* tf)
{
    if (!tf) return -1;
    EnsureItemMetaTypes();
    if (!g_tfClass || !g_itemInfoClass) return -1;

    void* info = nullptr;
    void* fInfo = il2cpp_class_get_field_from_name(g_tfClass, "bdyy");
    if (!fInfo) return -1;
    il2cpp_field_get_value(tf, fInfo, &info);
    if (!info) return -1;

    int32_t key = -1;
    void* fKey = il2cpp_class_get_field_from_name(g_itemInfoClass, "ItemKey");
    if (fKey) il2cpp_field_get_value(info, fKey, &key);
    return key;
}

static int32_t ReadGradeFromTf(void* tf)
{
    if (!tf) return -1;
    EnsureItemMetaTypes();
    if (!g_tfClass || !g_itemInfoClass) return -1;

    void* info = nullptr;
    void* fInfo = il2cpp_class_get_field_from_name(g_tfClass, "bdyy");
    if (!fInfo) return -1;
    il2cpp_field_get_value(tf, fInfo, &info);
    if (!info) return -1;

    int32_t grade = -1;
    void* fGr = il2cpp_class_get_field_from_name(g_itemInfoClass, "GRADE");
    if (!fGr) fGr = il2cpp_class_get_field_from_name(g_itemInfoClass, "Grade");
    if (fGr) il2cpp_field_get_value(info, fGr, &grade);
    return grade;
}

static bool ResolveSaveTypes()
{
    if (!g_domain) return false;
    if (!g_ysClass) g_ysClass = FindClass(g_domain, "", "ys");
    if (!g_playerSaveClass)
    {
        g_playerSaveClass = FindClass(g_domain, "TaskbarHero", "PlayerSaveData");
        if (!g_playerSaveClass) g_playerSaveClass = FindClassByNameContains(g_domain, "PlayerSaveData");
    }
    if (!g_itemSaveClass)
    {
        g_itemSaveClass = FindClass(g_domain, "TaskbarHero.EasySaveData", "ItemSaveData");
        if (!g_itemSaveClass) g_itemSaveClass = FindClassByNameContains(g_domain, "ItemSaveData");
    }
    if (g_ysClass && !g_lqkMethod)
        g_lqkMethod = FindMethodOnClass(g_ysClass, "lqk", 0);
    return g_playerSaveClass != nullptr && g_itemSaveClass != nullptr;
}

static bool PlayerSaveHasItemList(void* playerSave)
{
    if (!playerSave || !g_playerSaveClass) return false;
    void* list = nullptr;
    void* fList = il2cpp_class_get_field_from_name(g_playerSaveClass, "itemSaveDatas");
    if (!fList) return false;
    il2cpp_field_get_value(playerSave, fList, &list);
    return list != nullptr;
}

static void* ResolveYsInstance()
{
    if (g_ysInstance) return g_ysInstance;
    if (!ResolveSaveTypes() || !g_ysClass) return nullptr;

    void* fBfkm = il2cpp_class_get_field_from_name(g_ysClass, "bfkm");

    // nn`1 singleton static bbwm on parent chain
    if (il2cpp_class_get_parent)
    {
        for (void* k = g_ysClass; k; k = il2cpp_class_get_parent(k))
        {
        static const char* kInstFields[] = { "bbwm", "Instance", "instance", nullptr };
        for (int fi = 0; kInstFields[fi]; ++fi)
        {
            const char* fn = kInstFields[fi];
            void* cand = TryStaticField(k, fn);
                if (!cand || !fBfkm) continue;
                void* ps = nullptr;
                il2cpp_field_get_value(cand, fBfkm, &ps);
                if (PlayerSaveHasItemList(ps))
                {
                    g_ysInstance = cand;
                    return cand;
                }
            }
        }
    }

    // Scan all static fields for a ys live object with populated save data
    if (il2cpp_domain_get_assemblies && il2cpp_class_get_fields && fBfkm)
    {
        size_t asmCount = 0;
        void** assemblies = il2cpp_domain_get_assemblies(g_domain, &asmCount);
        for (size_t a = 0; a < asmCount; ++a)
        {
            void* img = il2cpp_assembly_get_image(assemblies[a]);
            if (!img || !il2cpp_image_get_class_count) continue;
            size_t n = il2cpp_image_get_class_count(img);
            for (size_t i = 0; i < n; ++i)
            {
                void* klass = il2cpp_image_get_class(img, i);
                if (!klass) continue;
                void* iter = nullptr;
                void* field = nullptr;
                while ((field = il2cpp_class_get_fields(klass, &iter)) != nullptr)
                {
                    if (il2cpp_field_get_flags && (il2cpp_field_get_flags(field) & 0x0010) == 0)
                        continue;
                    void* obj = nullptr;
                    il2cpp_field_static_get_value(field, &obj);
                    if (!obj) continue;
                    if (il2cpp_object_get_class && il2cpp_object_get_class(obj) != g_ysClass)
                        continue;
                    void* ps = nullptr;
                    il2cpp_field_get_value(obj, fBfkm, &ps);
                    if (PlayerSaveHasItemList(ps))
                    {
                        g_ysInstance = obj;
                        return obj;
                    }
                }
            }
        }
    }
    return nullptr;
}

static void* GetPlayerSaveData()
{
    if (!ResolveSaveTypes()) return nullptr;

    void* ys = ResolveYsInstance();
    if (ys && g_lqkMethod)
    {
        void* exc = nullptr;
        void* ps = il2cpp_runtime_invoke(g_lqkMethod, ys, nullptr, &exc);
        if (!exc && ps) return ps;
    }
    if (ys)
    {
        void* ps = nullptr;
        void* f = il2cpp_class_get_field_from_name(g_ysClass, "bfkm");
        if (f)
        {
            il2cpp_field_get_value(ys, f, &ps);
            if (ps) return ps;
        }
    }
    return nullptr;
}

static void* ReadListItem(void* list, int32_t index)
{
    if (!list) return nullptr;
    void* items = *reinterpret_cast<void**>(reinterpret_cast<uint8_t*>(list) + 0x10);
    if (!items) return nullptr;
    if (il2cpp_array_get)
    {
        void* elem = il2cpp_array_get(items, sizeof(void*), (size_t)index);
        if (elem) return *reinterpret_cast<void**>(elem);
    }
    return *reinterpret_cast<void**>(reinterpret_cast<uint8_t*>(items) + 0x20 + index * (int)sizeof(void*));
}

static int32_t ListSize(void* list)
{
    if (!list) return 0;
    return *reinterpret_cast<int32_t*>(reinterpret_cast<uint8_t*>(list) + 0x18);
}

static int32_t LookupItemKeyFromSave(uint64_t uid)
{
    void* playerSave = GetPlayerSaveData();
    if (!playerSave || !g_playerSaveClass || !g_itemSaveClass) return -1;

    void* list = nullptr;
    void* fList = il2cpp_class_get_field_from_name(g_playerSaveClass, "itemSaveDatas");
    if (!fList) return -1;
    il2cpp_field_get_value(playerSave, fList, &list);
    if (!list) return -1;

    int32_t size = ListSize(list);
    for (int32_t i = 0; i < size && i < 20000; ++i)
    {
        void* itemSave = ReadListItem(list, i);
        if (!itemSave) continue;

        uint64_t id = *reinterpret_cast<uint64_t*>(reinterpret_cast<uint8_t*>(itemSave) + 0x18);
        if (id != uid) continue;

        return *reinterpret_cast<int32_t*>(reinterpret_cast<uint8_t*>(itemSave) + 0x10);
    }
    return -1;
}

static int32_t ReadGradeFromItemKey(int32_t itemKey)
{
    if (itemKey <= 0) return -1;
    EnsureItemMetaTypes();
    void* info = ResolveItemInfo(itemKey);
    if (!info || !g_itemInfoClass) return -1;
    int32_t grade = -1;
    void* fGr = il2cpp_class_get_field_from_name(g_itemInfoClass, "GRADE");
    if (!fGr) fGr = il2cpp_class_get_field_from_name(g_itemInfoClass, "Grade");
    if (fGr) il2cpp_field_get_value(info, fGr, &grade);
    return grade;
}

static void* TfFromDictionary(uint64_t uid)
{
    if (!g_tiClass || !il2cpp_object_get_class) return nullptr;
    void* dict = TryStaticField(g_tiClass, "beaj");
    if (!dict) return nullptr;

    void* outTf = nullptr;
    void* args[2] = { &uid, &outTf };
    void* exc = nullptr;
    void* dictClass = il2cpp_object_get_class(dict);
    if (!dictClass) return nullptr;
    void* m = il2cpp_class_get_method_from_name(dictClass, "TryGetValue", 2);
    if (!m) return nullptr;
    il2cpp_runtime_invoke(m, dict, args, &exc);
    if (exc) return nullptr;
    return outTf;
}

static void* TfFromUid(uint64_t uid)
{
    void* args[1] = { &uid };
    void* exc = nullptr;

    void* methods[4] = { g_ishMethod, g_opdMethod, g_islMethod, g_esxMethod };
    for (int i = 0; i < 4; ++i)
    {
        if (!methods[i]) continue;
        exc = nullptr;
        void* tf = il2cpp_runtime_invoke(methods[i], nullptr, args, &exc);
        if (!exc && tf) return tf;
    }

    return TfFromDictionary(uid);
}

static int32_t LookupItemKey(uint64_t uid, char* srcBuf, size_t srcCap)
{
    if (srcBuf && srcCap > 0) srcBuf[0] = '\0';

    void* tf = TfFromUid(uid);
    if (tf)
    {
        int32_t key = ReadItemKeyFromTf(tf);
        if (key > 0)
        {
            if (srcBuf && srcCap > 0) snprintf(srcBuf, srcCap, "tf");
            return key;
        }
    }

    int32_t saveKey = LookupItemKeyFromSave(uid);
    if (saveKey > 0)
    {
        if (srcBuf && srcCap > 0) snprintf(srcBuf, srcCap, "save");
        return saveKey;
    }
    return -1;
}

static const char* GradeTypeName(int32_t grade)
{
    switch (grade)
    {
    case 0:  return "Common";
    case 1:  return "Uncommon";
    case 2:  return "Rare";
    case 3:  return "Legendary";
    case 4:  return "Immortal";
    case 5:  return "Arcana";
    case 6:  return "Beyond";
    case 7:  return "Celestial";
    case 8:  return "Divine";
    case 9:  return "Cosmic";
    case 10: return "None";
    default: return "?";
    }
}

static int32_t LookupGrade(uint64_t uid, int32_t itemKey)
{
    void* tf = TfFromUid(uid);
    if (tf)
    {
        int32_t grade = ReadGradeFromTf(tf);
        if (grade >= 0) return grade;
    }
    return ReadGradeFromItemKey(itemKey);
}

static int32_t ReadSaveSlotIndex(void* cacheObj)
{
    if (!cacheObj || !g_stashCacheClass || !g_stashSaveClass) return -1;
    void* save = nullptr;
    void* fSave = il2cpp_class_get_field_from_name(g_stashCacheClass, "beeg");
    if (!fSave) return -1;
    il2cpp_field_get_value(cacheObj, fSave, &save);
    if (!save) return -1;
    int32_t idx = -1;
    void* fIdx = il2cpp_class_get_field_from_name(g_stashSaveClass, "Index");
    if (fIdx) il2cpp_field_get_value(save, fIdx, &idx);
    return idx;
}

static size_t ScanAppend(char* buf, size_t cap, size_t pos, const char* fmt, ...)
{
    if (pos >= cap) return pos;
    va_list ap;
    va_start(ap, fmt);
    int n = vsnprintf(buf + pos, cap - pos, fmt, ap);
    va_end(ap);
    if (n < 0) return pos;
    return pos + (size_t)n;
}

static float ReadObscuredFloat(void* obj, size_t fieldOffset)
{
    uint8_t* p = reinterpret_cast<uint8_t*>(obj) + fieldOffset;
    uint32_t bits = *reinterpret_cast<uint32_t*>(p + 0x04) ^
                    *reinterpret_cast<uint32_t*>(p + 0x08);
    float value = 0.0f;
    memcpy(&value, &bits, sizeof(value));
    return value;
}

static float ReadObscuredFloatFake(void* obj, size_t fieldOffset)
{
    return *reinterpret_cast<float*>(reinterpret_cast<uint8_t*>(obj) + fieldOffset + 0x0C);
}

static int32_t ReadObscuredInt(void* obj, size_t fieldOffset)
{
    uint8_t* p = reinterpret_cast<uint8_t*>(obj) + fieldOffset;
    return *reinterpret_cast<int32_t*>(p + 0x04) ^
           *reinterpret_cast<int32_t*>(p + 0x08);
}

static int32_t ReadObscuredIntFake(void* obj, size_t fieldOffset)
{
    return *reinterpret_cast<int32_t*>(reinterpret_cast<uint8_t*>(obj) + fieldOffset + 0x0C);
}

static int32_t ManagedArrayLength(void* array)
{
    if (!array) return 0;
    if (il2cpp_array_length) return static_cast<int32_t>(il2cpp_array_length(array));
    return static_cast<int32_t>(*reinterpret_cast<uintptr_t*>(reinterpret_cast<uint8_t*>(array) + 0x18));
}

static void* ReadManagedArrayRef(void* array, int32_t index)
{
    if (!array || index < 0) return nullptr;
    if (il2cpp_array_get) return il2cpp_array_get(array, sizeof(void*), static_cast<size_t>(index));
    return *reinterpret_cast<void**>(reinterpret_cast<uint8_t*>(array) + 0x20 + static_cast<size_t>(index) * sizeof(void*));
}

static bool InvokeFloat0(void* obj, const char* methodName, float& value)
{
    if (!obj || !methodName || !il2cpp_object_get_class) return false;
    void* method = nullptr;
    for (void* k = il2cpp_object_get_class(obj); k; k = il2cpp_class_get_parent ? il2cpp_class_get_parent(k) : nullptr)
    {
        method = FindMethodOnClass(k, methodName, 0);
        if (method || !il2cpp_class_get_parent) break;
    }
    if (!method) return false;
    void* exc = nullptr;
    void* boxed = il2cpp_runtime_invoke(method, obj, nullptr, &exc);
    if (exc || !boxed) return false;
    value = *reinterpret_cast<float*>(reinterpret_cast<uint8_t*>(boxed) + 0x10);
    return true;
}

static bool InvokeDamageInfoOrigin0(void* obj, const char* methodName, float& value)
{
    if (!obj || !methodName || !il2cpp_object_get_class) return false;
    void* method = nullptr;
    for (void* k = il2cpp_object_get_class(obj); k; k = il2cpp_class_get_parent ? il2cpp_class_get_parent(k) : nullptr)
    {
        method = FindMethodOnClass(k, methodName, 0);
        if (method || !il2cpp_class_get_parent) break;
    }
    if (!method) return false;
    void* exc = nullptr;
    void* boxed = il2cpp_runtime_invoke(method, obj, nullptr, &exc);
    if (exc || !boxed) return false;
    value = *reinterpret_cast<float*>(reinterpret_cast<uint8_t*>(boxed) + 0x18);
    return true;
}

static bool InvokeCachedDamageInfoOrigin(const char* methodName, void* cache, float& value)
{
    if (!methodName || !cache) return false;
    void* klass = FindClass(g_domain, "", "uh");
    void* method = klass ? FindMethodOnClass(klass, methodName, 1) : nullptr;
    if (!method) return false;
    void* args[1] = { &cache };
    void* exc = nullptr;
    void* boxed = il2cpp_runtime_invoke(method, nullptr, args, &exc);
    if (exc || !boxed) return false;
    value = *reinterpret_cast<float*>(reinterpret_cast<uint8_t*>(boxed) + 0x18);
    return true;
}

static void* ResolveStageManager()
{
    void* klass = FindClass(g_domain, "TaskbarHero", "StageManager");
    if (!klass) klass = FindClass(g_domain, "", "StageManager");
    if (!klass) return nullptr;

    static const char* kInstFields[] = { "bbwm", "Instance", "instance", nullptr };
    for (void* k = klass; k; k = il2cpp_class_get_parent ? il2cpp_class_get_parent(k) : nullptr)
    {
        for (int i = 0; kInstFields[i]; ++i)
        {
            void* candidate = TryStaticField(k, kInstFields[i]);
            if (candidate) return candidate;
        }
        if (!il2cpp_class_get_parent) break;
    }
    return nullptr;
}

static void* ResolveHeroByIndex(int32_t index)
{
    if (index < 0) return nullptr;
    void* stageClass = FindClass(g_domain, "TaskbarHero", "StageManager");
    if (!stageClass) stageClass = FindClass(g_domain, "", "StageManager");
    void* stage = ResolveStageManager();
    if (!stageClass || !stage) return nullptr;

    void* heroListField = FindFieldOnClassOrParents(stageClass, "HeroList");
    void* heroes = nullptr;
    if (heroListField) il2cpp_field_get_value(stage, heroListField, &heroes);
    int32_t count = ManagedArrayLength(heroes);
    return index < count ? ReadManagedArrayRef(heroes, index) : nullptr;
}

static bool WriteObscuredFloat(void* obj, size_t fieldOffset, float value)
{
    if (!obj || !g_domain) return false;
    if (!g_obscuredFloatEncryptMethod)
    {
        void* klass = FindClass(g_domain, "CodeStage.AntiCheat.ObscuredTypes", "ObscuredFloat");
        g_obscuredFloatEncryptMethod = klass ? FindMethodOnClass(klass, "xat", 2) : nullptr;
    }
    if (!g_obscuredFloatEncryptMethod) return false;

    uint8_t* field = reinterpret_cast<uint8_t*>(obj) + fieldOffset;
    int32_t key = *reinterpret_cast<int32_t*>(field + 0x08);
    void* args[2] = { &value, &key };
    void* exc = nullptr;
    void* boxed = il2cpp_runtime_invoke(g_obscuredFloatEncryptMethod, nullptr, args, &exc);
    if (exc || !boxed) return false;

    int32_t hidden = *reinterpret_cast<int32_t*>(reinterpret_cast<uint8_t*>(boxed) + 0x10);
    *reinterpret_cast<int32_t*>(field + 0x04) = hidden;
    *reinterpret_cast<float*>(field + 0x0C) = value;
    return true;
}

static bool ApplyHeroLocks(int32_t heroIndex)
{
    if (heroIndex < 0 || heroIndex >= 3) return false;
    HeroLocks& locks = g_heroLocks[heroIndex];
    void* hero = ResolveHeroByIndex(heroIndex);
    if (!hero) return false;

    if (locks.attackDamage) WriteObscuredFloat(hero, 0x104, locks.attackDamageValue);
    if (locks.attackSpeed) WriteObscuredFloat(hero, 0x118, locks.attackSpeedValue);

    if (locks.currentHp || locks.maxHp)
    {
        void* heroClass = il2cpp_object_get_class ? il2cpp_object_get_class(hero) : nullptr;
        void* healthField = FindFieldOnClassOrParents(heroClass, "UnitHealthController");
        void* health = nullptr;
        if (healthField) il2cpp_field_get_value(hero, healthField, &health);
        if (health)
        {
            uint8_t* hp = reinterpret_cast<uint8_t*>(health);
            if (locks.currentHp) *reinterpret_cast<float*>(hp + 0x40) = locks.currentHpValue;
            if (locks.maxHp) *reinterpret_cast<float*>(hp + 0x4C) = locks.maxHpValue;
        }
    }
    return true;
}

static void RunHeroCommand()
{
    if (!g_heroScan) return;
    g_heroScan->done = 0;
    g_heroScan->length = 0;
    memset(g_heroScan->text, 0, sizeof(g_heroScan->text));

    void* hero = ResolveHeroByIndex(g_heroScan->heroIndex);
    bool ok = false;
    const char* label = "unknown";
    if (g_heroScan->command == 6 && g_heroScan->heroIndex >= 0 && g_heroScan->heroIndex < 3)
    {
        memset(&g_heroLocks[g_heroScan->heroIndex], 0, sizeof(HeroLocks));
        ok = true;
        label = "All Locks Cleared";
    }
    else if (hero)
    {
        if (g_heroScan->command == 1 || g_heroScan->command == 2 || g_heroScan->command == 5)
        {
            void* heroClass = il2cpp_object_get_class ? il2cpp_object_get_class(hero) : nullptr;
            void* healthField = FindFieldOnClassOrParents(heroClass, "UnitHealthController");
            void* health = nullptr;
            if (healthField) il2cpp_field_get_value(hero, healthField, &health);
            if (health)
            {
                uint8_t* hp = reinterpret_cast<uint8_t*>(health);
                if (g_heroScan->command == 1 || g_heroScan->command == 5)
                    *reinterpret_cast<float*>(hp + 0x40) = g_heroScan->value;
                if (g_heroScan->command == 2 || g_heroScan->command == 5)
                    *reinterpret_cast<float*>(hp + 0x4C) = g_heroScan->value;
                ok = true;
                label = g_heroScan->command == 1 ? "Current HP" :
                        g_heroScan->command == 2 ? "Max HP" : "Current + Max HP";
                HeroLocks& locks = g_heroLocks[g_heroScan->heroIndex];
                if (g_heroScan->command == 1 || g_heroScan->command == 5)
                {
                    locks.currentHp = true;
                    locks.currentHpValue = g_heroScan->value;
                }
                if (g_heroScan->command == 2 || g_heroScan->command == 5)
                {
                    locks.maxHp = true;
                    locks.maxHpValue = g_heroScan->value;
                }
            }
        }
        else if (g_heroScan->command == 3)
        {
            ok = WriteObscuredFloat(hero, 0x104, g_heroScan->value);
            label = "Attack Damage";
            if (ok)
            {
                g_heroLocks[g_heroScan->heroIndex].attackDamage = true;
                g_heroLocks[g_heroScan->heroIndex].attackDamageValue = g_heroScan->value;
            }
        }
        else if (g_heroScan->command == 4)
        {
            ok = WriteObscuredFloat(hero, 0x118, g_heroScan->value);
            label = "Attack Speed";
            if (ok)
            {
                g_heroLocks[g_heroScan->heroIndex].attackSpeed = true;
                g_heroLocks[g_heroScan->heroIndex].attackSpeedValue = g_heroScan->value;
            }
        }
    }

    size_t pos = ScanAppend(g_heroScan->text, sizeof(g_heroScan->text), 0,
        "%s UI Hero %d (index %d) %s = %.6g\r\n", ok ? "OK:" : "ERROR:",
        g_heroScan->heroIndex + 1, g_heroScan->heroIndex, label, g_heroScan->value);
    g_heroScan->length = static_cast<int32_t>(pos);
    g_heroScan->done = ok ? 1 : -1;
}

static void RunHeroScan()
{
    if (!g_heroScan) return;
    g_heroScan->done = 0;
    g_heroScan->length = 0;
    memset(g_heroScan->text, 0, sizeof(g_heroScan->text));

    char* text = g_heroScan->text;
    const size_t cap = sizeof(g_heroScan->text);
    size_t pos = ScanAppend(text, cap, 0,
        "=== TBH 1.00.09 hero diagnostics (read-only) ===\r\n"
        "Candidate labels are inferred from the dump and require runtime confirmation.\r\n\r\n");

    void* stageClass = FindClass(g_domain, "TaskbarHero", "StageManager");
    if (!stageClass) stageClass = FindClass(g_domain, "", "StageManager");
    void* stage = ResolveStageManager();
    if (!stageClass || !stage)
    {
        pos = ScanAppend(text, cap, pos, "ERROR: StageManager singleton not resolved. Enter a run and retry.\r\n");
        g_heroScan->length = static_cast<int32_t>(pos);
        g_heroScan->done = -1;
        return;
    }

    void* heroListField = FindFieldOnClassOrParents(stageClass, "HeroList");
    void* heroes = nullptr;
    if (heroListField) il2cpp_field_get_value(stage, heroListField, &heroes);
    int32_t count = ManagedArrayLength(heroes);
    pos = ScanAppend(text, cap, pos, "StageManager=0x%llX HeroList=0x%llX count=%d\r\n",
        (unsigned long long)stage, (unsigned long long)heroes, count);
    if (!heroes || count <= 0 || count > 64)
    {
        pos = ScanAppend(text, cap, pos, "ERROR: HeroList unavailable or unexpected layout. Ensure a hero is active.\r\n");
        g_heroScan->length = static_cast<int32_t>(pos);
        g_heroScan->done = -1;
        return;
    }

    static const struct { const char* name; size_t offset; } kStats[] =
    {
        { "AttackDamage", 0x104 }, { "AttackSpeed", 0x118 },
        { "CastSpeed", 0x12C }, { "Armor", 0x140 },
        { "MoveSpeedInternal", 0x154 }, { "CriticalChance", 0x168 },
        { "CriticalDamage", 0x17C }, { "CooldownReduction", 0x190 },
        { "Unknown_1A4", 0x1A4 }, { "Unknown_1B8", 0x1B8 },
        { "SkillRangeExpansion", 0x1CC }, { "FireResistance", 0x1E0 },
    };
    static const struct { const char* name; const char* method; float displayScale; } kGetters[] =
    {
        { "Attack Damage", "gqg", 1.0f },
        { "Attack Speed", "gmv", 1.0f },
        { "Armor", "gqh", 1.0f },
        { "Move Speed", "gqi", 100.0f },
        { "Max HP", "gqj", 1.0f },
        { "Critical Chance", "gqk", 100.0f },
        { "Critical Damage", "gql", 100.0f },
        { "Cooldown Reduction", "gqm", 100.0f },
        { "Cast Speed", "gqn", 1.0f },
    };

    for (int32_t i = 0; i < count; ++i)
    {
        void* hero = ReadManagedArrayRef(heroes, i);
        if (!hero) continue;
        void* heroClass = il2cpp_object_get_class ? il2cpp_object_get_class(hero) : nullptr;
        const char* className = heroClass && il2cpp_class_get_name ? il2cpp_class_get_name(heroClass) : "?";
        bool isHero = *reinterpret_cast<bool*>(reinterpret_cast<uint8_t*>(hero) + 0x100);
        pos = ScanAppend(text, cap, pos, "\r\nHERO[%d] (UI Hero %d) object=0x%llX class=%s isHero=%d\r\n",
            i, i + 1, (unsigned long long)hero, className ? className : "?", isHero ? 1 : 0);

        float runtimeAttackDamage = 0.0f;
        float runtimeAttackSpeed = 0.0f;
        InvokeFloat0(hero, "gqg", runtimeAttackDamage);
        InvokeFloat0(hero, "gmv", runtimeAttackSpeed);
        for (const auto& stat : kStats)
            pos = ScanAppend(text, cap, pos, "  field %-26s +0x%03llX decrypt=%.6g fake=%.6g\r\n",
                stat.name, (unsigned long long)stat.offset, ReadObscuredFloat(hero, stat.offset),
                ReadObscuredFloatFake(hero, stat.offset));
        pos = ScanAppend(text, cap, pos, "  -- Unit runtime getters --\r\n");
        for (const auto& getter : kGetters)
        {
            float value = 0.0f;
            bool ok = InvokeFloat0(hero, getter.method, value);
            pos = ScanAppend(text, cap, pos, "  runtime %-26s %s = %s%.6g (display %.6g)\r\n",
                getter.name, getter.method, ok ? "" : "unresolved/", value, value * getter.displayScale);
        }
        pos = ScanAppend(text, cap, pos, "  runtime BasicAttackDPS formula = %.6g\r\n",
            runtimeAttackDamage * runtimeAttackSpeed);
        float grfOrigin = 0.0f;
        bool hasGrfOrigin = InvokeDamageInfoOrigin0(hero, "grf", grfOrigin);
        pos = ScanAppend(text, cap, pos, "  damage payload Unit.grf OriginDamage = %s%.6g\r\n",
            hasGrfOrigin ? "" : "unresolved/", grfOrigin);

        void* healthField = FindFieldOnClassOrParents(heroClass, "UnitHealthController");
        void* health = nullptr;
        if (healthField) il2cpp_field_get_value(hero, healthField, &health);
        pos = ScanAppend(text, cap, pos, "  healthController=0x%llX\r\n", (unsigned long long)health);
        if (health)
        {
            uint8_t* hp = reinterpret_cast<uint8_t*>(health);
            pos = ScanAppend(text, cap, pos,
                "  HP raw: +38=%.6g +3C=%.6g +40=%.6g +44=%.6g +48=%.6g +4C=%.6g\r\n",
                *reinterpret_cast<float*>(hp + 0x38), *reinterpret_cast<float*>(hp + 0x3C),
                *reinterpret_cast<float*>(hp + 0x40), *reinterpret_cast<float*>(hp + 0x44),
                *reinterpret_cast<float*>(hp + 0x48), *reinterpret_cast<float*>(hp + 0x4C));
            float gtp = 0.0f, gtq = 0.0f;
            bool hasGtp = InvokeFloat0(health, "gtp", gtp);
            bool hasGtq = InvokeFloat0(health, "gtq", gtq);
            pos = ScanAppend(text, cap, pos, "  HP getters: gtp=%s%.6g gtq=%s%.6g\r\n",
                hasGtp ? "" : "unresolved/", gtp, hasGtq ? "" : "unresolved/", gtq);
            pos = ScanAppend(text, cap, pos,
                "  CONFIRMED Current HP: controller+0x40 = %.6g | Max HP: controller+0x4C = %.6g\r\n",
                *reinterpret_cast<float*>(hp + 0x40), *reinterpret_cast<float*>(hp + 0x4C));
        }

        void* cache = *reinterpret_cast<void**>(reinterpret_cast<uint8_t*>(hero) + 0x3A0);
        pos = ScanAppend(text, cap, pos, "  heroCache(+0x3A0)=0x%llX", (unsigned long long)cache);
        if (cache)
        {
            float jjeOrigin = 0.0f, jjdOrigin = 0.0f;
            bool hasJjeOrigin = InvokeCachedDamageInfoOrigin("jje", cache, jjeOrigin);
            bool hasJjdOrigin = InvokeCachedDamageInfoOrigin("jjd", cache, jjdOrigin);
            pos = ScanAppend(text, cap, pos,
                "  cached damage payloads: uh.jje OriginDamage=%s%.6g | uh.jjd OriginDamage=%s%.6g\r\n",
                hasJjeOrigin ? "" : "unresolved/", jjeOrigin, hasJjdOrigin ? "" : "unresolved/", jjdOrigin);
            pos = ScanAppend(text, cap, pos,
                " level candidates decrypt/fake: +CC=%d/%d +DC=%d/%d +EC=%d/%d +FC=%d/%d +138=%d/%d\r\n",
                ReadObscuredInt(cache, 0xCC), ReadObscuredIntFake(cache, 0xCC),
                ReadObscuredInt(cache, 0xDC), ReadObscuredIntFake(cache, 0xDC),
                ReadObscuredInt(cache, 0xEC), ReadObscuredIntFake(cache, 0xEC),
                ReadObscuredInt(cache, 0xFC), ReadObscuredIntFake(cache, 0xFC),
                ReadObscuredInt(cache, 0x138), ReadObscuredIntFake(cache, 0x138));
            pos = ScanAppend(text, cap, pos, "  CONFIRMED Level candidate: cache+0xCC fakeValue = %d\r\n",
                ReadObscuredIntFake(cache, 0xCC));
        }
        else
            pos = ScanAppend(text, cap, pos, "\r\n");

        void* heroInfo = cache ? *reinterpret_cast<void**>(reinterpret_cast<uint8_t*>(cache) + 0x30) : nullptr;
        if (heroInfo)
        {
            uint8_t* info = reinterpret_cast<uint8_t*>(heroInfo);
            pos = ScanAppend(text, cap, pos,
                "  HeroInfoData=0x%llX base ints: ATK=%d AttackSpeed=%d CastSpeed=%d CritChance=%d CritDamage=%d CDR=%d MaxHP=%d Armor=%d MoveSpeed=%d\r\n",
                (unsigned long long)heroInfo,
                *reinterpret_cast<int32_t*>(info + 0x58), *reinterpret_cast<int32_t*>(info + 0x5C),
                *reinterpret_cast<int32_t*>(info + 0x60), *reinterpret_cast<int32_t*>(info + 0x64),
                *reinterpret_cast<int32_t*>(info + 0x68), *reinterpret_cast<int32_t*>(info + 0x6C),
                *reinterpret_cast<int32_t*>(info + 0x70), *reinterpret_cast<int32_t*>(info + 0x74),
                *reinterpret_cast<int32_t*>(info + 0x78));
        }
    }

    pos = ScanAppend(text, cap, pos, "\r\nScan complete. Compare candidates with the in-game hero panel.\r\n");
    g_heroScan->length = static_cast<int32_t>(pos < cap ? pos : cap - 1);
    g_heroScan->done = 1;
}

static bool ScanUidLine(char* text, size_t cap, size_t& pos, const char* source, uint64_t uid, bool* seen, int seenCap)
{
    int hash = (int)((uid >> 4) % (uint64_t)seenCap);
    if (seen[hash]) return false;
    seen[hash] = true;

    int32_t key = LookupItemKey(uid, nullptr, 0);
    int32_t grade = LookupGrade(uid, key);

    pos = ScanAppend(text, cap, pos, "%s uid=0x%llX itemKey=%d grade=%d (%s)\r\n",
                     source, (unsigned long long)uid, key, grade, GradeTypeName(grade));
    return true;
}

static void RunItemCatalogDump()
{
    if (!g_scan) return;
    g_scan->done = 0;
    g_scan->length = 0;
    memset(g_scan->text, 0, sizeof(g_scan->text));

    EnsureItemMetaTypes();
    ResolveItemAdd();
    void* yq = ResolveYqInstance();

    char* text = g_scan->text;
    const size_t cap = sizeof(g_scan->text);
    size_t pos = 0;
    int32_t count = 0;
    bool truncated = false;

    pos = ScanAppend(text, cap, pos,
        "=== TBH item catalog (all ItemInfoData from yq data manager) ===\r\n"
        "Format: ITEM itemKey grade=N (Name)\r\n\r\n");

    if (!yq)
    {
        pos = ScanAppend(text, cap, pos,
            "ERROR: yq data manager not found (load into main menu / stash first).\r\n");
        g_scan->length = (int32_t)pos;
        g_scan->done = -1;
        return;
    }

    pos = CatalogAppendFromList(yq, text, cap, pos, count, truncated);
    if (count == 0)
        pos = CatalogAppendFromDict(yq, text, cap, pos, count, truncated);

    pos = ScanAppend(text, cap, pos, "\r\nTotal: %d item(s).", count);
    if (truncated)
        pos = ScanAppend(text, cap, pos, " (TRUNCATED — increase scan buffer or export partial)");
    pos = ScanAppend(text, cap, pos, "\r\n");

    g_scan->length = (int32_t)(pos < cap ? pos : cap - 1);
    g_scan->done = truncated ? 2 : 1;
}

static void RunItemScan()
{
    if (!g_scan) return;
    g_scan->done = 0;
    g_scan->length = 0;
    memset(g_scan->text, 0, sizeof(g_scan->text));

    TryResolveStash();
    ResolveItemAdd();
    ResolveSaveTypes();
    EnsureItemMetaTypes();

    char* text = g_scan->text;
    const size_t cap = sizeof(g_scan->text);
    size_t pos = 0;
    bool seen[256] = {};

    void* playerSave = GetPlayerSaveData();
    void* itemList = nullptr;
    int32_t saveItemCount = 0;
    if (playerSave && g_playerSaveClass)
    {
        void* fList = il2cpp_class_get_field_from_name(g_playerSaveClass, "itemSaveDatas");
        if (fList)
        {
            il2cpp_field_get_value(playerSave, fList, &itemList);
            saveItemCount = ListSize(itemList);
        }
    }

    pos = ScanAppend(text, cap, pos,
        "=== TBH item scan (use itemKey in spawn) ===\r\n"
        "grade = EGradeType: 0 Common, 1 Uncommon, 2 Rare, 3 Legendary, 4 Immortal, 5 Arcana, 6 Beyond, 7 Celestial, 8 Divine, 9 Cosmic, 10 None\r\n"
        "src: tf=live item, save=PlayerSaveData.itemSaveDatas\r\n\r\n");
    pos = ScanAppend(text, cap, pos,
        "Debug: saveItems=%d ys=%s ish=%s saveTypes=%s\r\n\r\n",
        saveItemCount,
        ResolveYsInstance() ? "ok" : "no",
        g_ishMethod ? "ok" : "no",
        (g_playerSaveClass && g_itemSaveClass) ? "ok" : "no");

    int stashRows = 0;
    for (int32_t slot = 0; slot < kStashMaxSlots; ++slot)
    {
        void* cache = InvokeJbp(nullptr, slot);
        if (!cache) continue;
        uint64_t uid = ReadSaveUniqueId(cache);
        if (uid == 0) continue;

        int32_t idx = ReadSaveSlotIndex(cache);
        char src[16] = {};
        int32_t key = LookupItemKey(uid, src, sizeof(src));
        int32_t grade = LookupGrade(uid, key);

        pos = ScanAppend(text, cap, pos,
                         "STASH tabSlot=%2d saveIndex=%d uid=0x%llX itemKey=%d grade=%d (%s) src=%s\r\n",
                         slot, idx, (unsigned long long)uid, key, grade, GradeTypeName(grade),
                         src[0] ? src : "?");
        stashRows++;
    }

    int invRows = 0;
    if (g_iryMethod)
    {
        void* exc = nullptr;
        void* coll = il2cpp_runtime_invoke(g_iryMethod, nullptr, nullptr, &exc);
        if (!exc && coll && il2cpp_array_length)
        {
            uint32_t len = il2cpp_array_length(coll);
            pos = ScanAppend(text, cap, pos, "\r\n-- INVENTORY (ti.iry UIDs) --\r\n");
            for (uint32_t i = 0; i < len && i < 500u; ++i)
            {
                uint64_t uid = 0;
                if (il2cpp_array_get)
                {
                    void* elem = il2cpp_array_get(coll, sizeof(uint64_t), i);
                    if (elem) uid = *reinterpret_cast<uint64_t*>(elem);
                }
                if (uid == 0) continue;
                if (ScanUidLine(text, cap, pos, "INV", uid, seen, 256))
                    invRows++;
            }
        }
    }

    pos = ScanAppend(text, cap, pos,
        "\r\nSummary: %d stash slot(s), %d inventory UID(s).\r\n"
        "Pick an itemKey from above for Spawn (same grade optional).\r\n",
        stashRows, invRows);

    g_scan->length = (int32_t)(pos < cap ? pos : cap - 1);
    g_scan->done = 1;
}

static bool SpawnItem(int32_t itemKey, int32_t grade)
{
    g_lastSpawnDetail = 0;
    TryResolveStash();

    int32_t catalogGrade = -1;
    int32_t spawnKey = ResolveSpawnItemKey(itemKey, grade, &catalogGrade);
    const int32_t keyDigit = KeyGradeDigit(itemKey);
    const bool gradeMismatch = (grade >= 0 && spawnKey == itemKey &&
                                keyDigit != grade &&
                                catalogGrade >= 0 && catalogGrade != grade);

    if (gradeMismatch)
    {
        g_lastSpawnDetail = -7;
        return false;
    }

    if (!ItemKeyExists(spawnKey))
    {
        g_lastSpawnDetail = -2;
        return false;
    }

    // Fast path: ti.blk / ti.isk (inventory). Stash path only if both fail.
    if (SpawnViaAddApis(spawnKey))
        return true;

    if (SpawnViaJbo(spawnKey, -1)) return true;
    if (SpawnViaStash(spawnKey, -1)) return true;

    if (g_lastSpawnDetail == 0) g_lastSpawnDetail = -1;
    return false;
}

static DWORD WINAPI WorkerThread(LPVOID)
{
    for (int tries = 0; tries < 200; ++tries)
    {
        if (LoadIl2Cpp()) break;
        Sleep(50);
    }
    if (!il2cpp_domain_get) return 0;

    void* domain = il2cpp_domain_get();
    if (!domain) return 0;
    g_domain = domain;
    il2cpp_thread_attach(domain);

    HANDLE hMap = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE,
                                     0, kMapSize, kMapName);
    if (!hMap) return 0;
    g_shared = static_cast<SharedState*>(
        MapViewOfFile(hMap, FILE_MAP_ALL_ACCESS, 0, 0, kMapSize));
    if (!g_shared) return 0;
    g_shared->magic       = kMagic;
    g_shared->spawnResult = 0;

    HANDLE hScanMap = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE,
                                         0, kScanMapSize, kScanMapName);
    if (hScanMap)
    {
        g_scan = static_cast<ScanState*>(MapViewOfFile(hScanMap, FILE_MAP_ALL_ACCESS, 0, 0, kScanMapSize));
        if (g_scan)
        {
            g_scan->magic = kScanMagic;
            g_scan->done = 0;
            g_scan->length = 0;
            g_scan->text[0] = '\0';
        }
    }

    HANDLE hHeroScanMap = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE,
                                             0, kScanMapSize, kHeroScanMapName);
    if (hHeroScanMap)
    {
        g_heroScan = static_cast<HeroScanState*>(MapViewOfFile(hHeroScanMap, FILE_MAP_ALL_ACCESS, 0, 0, kScanMapSize));
        if (g_heroScan)
        {
            g_heroScan->magic = kHeroScanMagic;
            g_heroScan->done = 0;
            g_heroScan->length = 0;
            g_heroScan->text[0] = '\0';
        }
    }

    int32_t statusFlags = 1;

    g_timeClass = FindClass(domain, "UnityEngine", "Time");
    if (g_timeClass)
    {
        g_setTimeScale = il2cpp_class_get_method_from_name(g_timeClass, "set_timeScale", 1);
        if (g_setTimeScale) statusFlags |= 2;
    }

    TryResolveStash();
    statusFlags = BuildStatusFlags();
    g_shared->status = statusFlags;
    g_lastSpawnReq   = g_shared->spawnRequest;

    bool wasEnabled = false;

    for (;;)
    {
        g_shared->heartbeat++;

        if (g_shared->speedEnabled)
        {
            float ts = g_shared->timeScale;
            if (ts < 0.0f)   ts = 0.0f;
            if (ts > 500.0f) ts = 500.0f;
            SetTimeScale(ts);
            wasEnabled = true;
        }
        else if (wasEnabled)
        {
            SetTimeScale(1.0f);
            wasEnabled = false;
        }

        // Re-resolve stash (~1s) and pick up trainer-pushed RVAs for new game builds
        if ((++g_resolveCounter % 60) == 0)
        {
            TryResolveStash();
            for (int32_t heroIndex = 0; heroIndex < 3; ++heroIndex)
                ApplyHeroLocks(heroIndex);
        }

        int32_t curReq = g_shared->spawnRequest;
        if (curReq != g_lastSpawnReq)
        {
            int32_t reqKey = g_shared->spawnItemKey;
            int32_t reqGrade = g_shared->spawnGrade;
            if (g_shared->spawnRequest != curReq)
                continue;

            if (InterlockedCompareExchange(&g_spawnBusy, 1, 0) != 0)
            {
                g_lastSpawnReq = curReq;
                g_shared->spawnDetail = 0;
                g_shared->spawnResult = -8;
                continue;
            }

            g_lastSpawnReq = curReq;
            TryResolveStash();
            if (!g_stashInstance)
                g_stashInstance = ResolveStashInstance();

            bool ok = SpawnItem(reqKey, reqGrade);
            int32_t catalogGrade = -1;
            int32_t usedKey = ResolveSpawnItemKey(reqKey, reqGrade, &catalogGrade);
            if (ok)
                g_shared->spawnDetail = (usedKey != reqKey) ? usedKey : 0;
            else
            {
                if (g_lastSpawnDetail != 0)
                    g_shared->spawnDetail = g_lastSpawnDetail;
                else if (usedKey != reqKey)
                    g_shared->spawnDetail = usedKey;
                else
                    g_shared->spawnDetail = -1;
            }
            g_shared->spawnResult = ok ? 1 : (g_lastSpawnDetail != 0 ? g_lastSpawnDetail : -1);
            InterlockedExchange(&g_spawnBusy, 0);
        }

        if (g_scan && g_scan->request != g_lastScanReq)
        {
            g_lastScanReq = g_scan->request;
            if (g_scan->request & 1)
                RunItemCatalogDump();
            else
                RunItemScan();
        }

        if (g_heroScan && g_heroScan->request != g_lastHeroScanReq)
        {
            g_lastHeroScanReq = g_heroScan->request;
            if (g_heroScan->command == 0)
                RunHeroScan();
            else
                RunHeroCommand();
        }

        Sleep(16);
    }
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(hModule);
        CreateThread(nullptr, 0, WorkerThread, nullptr, 0, nullptr);
    }
    return TRUE;
}
