# Mono Dissector + Cheat Engine — Guía 1.00.09

Tu archivo `mono dissector list.TXT` lista **clases** de `Assembly-CSharp.dll` vistas por Cheat Engine.
Los números `264322551f0` son punteros a **metadatos Il2Cpp** en memoria, **no** offsets para el trainer.

Para **HP/ATK** y **spawn** necesitas o bien:
- RVAs en `GameBuildProfile` (ya rellenados para ACTk + stash desde `dump.cs`), o
- **Pointer scan** en CE mientras juegas.

---

## Clases confirmadas (tu lista = dump 1.00.09)

| Feature | Clase en Mono / Dump | Notas |
|---------|----------------------|--------|
| Stash | `ue.Stash`, `ue.StashCache` | Namespace pasó de `ud` → `ue` |
| Items | `ue.ti`, `ue.ti.tf` | Spawn vía `ue.ti.blk` (trainer ya lo usa) |
| Héroe en combate | `TaskbarHero.Hero` : `Unit` | Stats en `Unit` + `pf` |
| Stage / party | `TaskbarHero.StageManager` | `HeroList` @ offset `0x30` |
| HP bar logic | `pf` (`Unit.UnitHealthController`) | En `Unit` @ `0xB0` |
| UI stash | `TaskbarHero.UI_Stash` | Solo UI, no el backend |

---

## Offsets de campos útiles (desde dump.cs, no del TXT)

### `TaskbarHero.Unit`
| Offset | Campo | Uso |
|--------|-------|-----|
| `0xB0` | `pf UnitHealthController` | Ir al controlador de vida |
| `0x100` | `bool b_isHero` | Filtrar héroes |
| `0x104`+ | `ObscuredFloat bcfr…` | Stats ofuscados (ACTk) |

### `pf` (health controller)
| Offset | Campo | Probable |
|--------|-------|----------|
| `0x30` | `Unit bchp` | Referencia al Unit |
| `0x38` | `float bchq` | ? |
| `0x3C` | `float bchr` | HP actual (verificar en CE) |
| `0x44` | `float bcht` | HP máximo (verificar) |

Métodos: `gtp()` = get HP, `gtq()` = get max HP (RVAs en dump).

### `TaskbarHero.StageManager`
| Offset | Campo |
|--------|-------|
| `0x30` | `Hero[] HeroList` |

---

## Cómo sacar la cadena HP para `GameBuildProfile.V1009`

1. Abre el juego, entra a un **combate** con el héroe activo.
2. Cheat Engine → Attach `TaskBarHero.exe`.
3. **Mono** → Activate mono features → `Mono → Dissect mono`.
4. En `Assembly-CSharp.dll` expande:
   - `TaskbarHero.StageManager` → **static fields** (busca instancia/singleton).
   - O busca instancias de `TaskbarHero.Hero` / `TaskbarHero.Unit`.
5. En el héroe activo:
   - `Unit` → campo `UnitHealthController` (`pf`)
   - En `pf` busca floats que cambien al recibir daño (= HP).
6. Click derecho en el float de HP → **Pointer scan**.
7. Filtra punteros con base `GameAssembly.dll` + cadena estable.
8. La cadena típica del trainer es:
   ```
   GameAssembly + STATIC → +0x28 → +0x68 → … → +0x40  (float HP)
   ```
9. Pega en `GameBuildProfile.cs` → `V1009`:
   ```csharp
   HpStatic = 0x???????,
   HpOffsets = [ 0x28, 0x68, ... , 0x40 ],
   ```

Repite para **ATK** (suele ser otro `ObscuredFloat` en `Unit` o un float en `bcgw`/`Dictionary` de daño).

---

## Qué exportar de Mono Dissector (si quieres ayuda)

El TXT actual solo tiene nombres de clase. Para más valor, en CE:

1. Expande `ue.Stash` → **static fields** → copia nombres + offsets.
2. Expande `TaskbarHero.Unit` → **fields** → copia offsets de `UnitHealthController` y floats.
3. O exporta de nuevo con campos visibles.

Pega eso en el chat y actualizamos el código.

---

## Spawn de items (ya en código)

No hace falta Mono para spawn si el hook funciona:

| Método | RVA 1.00.09 |
|--------|-------------|
| `ue.ti.blk` (add item) | `0x8BF830` |
| `ue.Stash.jbn` (insert cache) | `0x8D62F0` |
| `ue.Stash.jbw` (slot por índice) | `0x8D75A0` |

Clases en tu lista: `26432251c10 : ue.ti`, `264322551f0 : ue.Stash`.

---

## ACTk (Mono lista `ACTk.Runtime.dll`)

El bypass usa **GameAssembly** + RVAs, no Assembly-CSharp:

| Detector | RVA 1.00.09 |
|----------|-------------|
| Obscured xvo/xvp/xvq | `0x6C11E0` / `0x6C1210` / `0x6C1360` |
| Injection xvf | `0x6C0EE0` |
| Speed Update / Pause | `0x6C6750` / `0x6C66C0` |

`ACTk.Runtime.dll` en Mono es solo metadata; el trainer parchea código nativo en `GameAssembly.dll`.

---

## Resumen

| Fuente | Sirve para |
|--------|------------|
| `mono dissector list.TXT` | Confirmar nombres de clases (`ue.*`, `TaskbarHero.*`) |
| `dump.cs` | RVAs de métodos + offsets de campos |
| Pointer scan en CE | `HpStatic` / `AtkStatic` para V1009 |
| Trainer actual | ACTk + stash spawn 1.00.09 listos tras rebuild |
