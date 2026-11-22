# 🎮 Xibalba — Documento de Contexto del Videojuego

> **Motor:** Unity (URP 2D) · **Lenguaje:** C# · **Género:** Plataformas 2D de acción con exploración por salas

---

## 1. Concepto General

**Xibalba** es un juego de plataformas 2D de acción con una estética inspirada en la mitología maya. El jugador explora un submundo dividido en salas interconectadas, derrota enemigos, recoge llaves y habilidades especiales, y progresa hasta vencer a un jefe final.

---

## 2. Estructura de Escenas

| Escena | Descripción |
|---|---|
| `Menu` | Menú principal (Play, Créditos, Controles, Salir) |
| `Controlls` | Pantalla de controles |
| `Credits` | Pantalla de créditos |
| `Room-02` | Primera sala de juego activa |
| `Level-01/` | Carpeta con salas de nivel (escenas .unity adicionales) |

Las salas se cargan mediante el `TransitionManager`, que gestiona la transición entre escenas con un fade de pantalla y coloca al jugador en el `SpawnPoint` correcto.

---

## 3. Controles del Jugador

| Acción | Tecla(s) |
|---|---|
| Mover izquierda | `A` / `←` |
| Mover derecha | `D` / `→` |
| Saltar | `Espacio` |
| Dash | `Shift izquierdo` |
| Ataque cuerpo a cuerpo | `J` |
| Lanzar arma | `K` |
| Interactuar / Checkpoint | `E` |

---

## 4. Sistema del Jugador

### 4.1 Movimiento (`PlayerController.cs`)

El `PlayerController` implementa `IDamageable` y es el núcleo del personaje.

**Parámetros de movimiento:**
- `walkSpeed` = 8 u/s
- `jumpForce` = 14, `doubleJumpForce` = 12
- `coyoteTime` = 0.1 s — permite saltar justo al caer del borde
- `jumpBufferTime` = 0.1 s — buffer de entrada para el salto
- Gravedad diferencial: caída más rápida (`fallGravityMultiplier` = 2.5×), salto corto (`lowJumpMultiplier` = 2×)

**Habilidades (se pueden bloquear/desbloquear en runtime):**

| Habilidad | Campo | Descripción |
|---|---|---|
| Doble Salto | `hasDoubleJump` | Segundo salto en el aire |
| Dash | `hasDash` | Desplazamiento horizontal rápido (inm. a daño durante dash) |
| Salto de Pared | `hasWallJump` | Deslizarse por paredes y saltar desde ellas |

**Dash:**
- `dashSpeed` = 20, `dashDuration` = 0.15 s, `dashCooldown` = 0.5 s
- Un dash aéreo por salto. Al deslizarse en pared, el dash aleja al jugador de ella.

**Salto de Pared:**
- Velocidad de deslizamiento = 2 u/s
- `wallJumpForce` = (10, 14) — empuje horizontal y vertical
- Tras un salto de pared se recuperan el doble salto y el dash aéreo.

### 4.2 Vida y Daño

- `maxHealth` = 5 corazones
- Al recibir daño: knockback, período de invulnerabilidad (1 s, con parpadeo), hit-stun 0.2 s
- Al caer fuera del mapa (Y < `fallLimitY`): teletransporte a la última posición segura
- Al morir: se activa `GameOverManager.ShowGameOver()` → pantalla DERROTA

**Respawn:**
- `Retry` → recarga la escena desde el último `Checkpoint` físico (o inicio de escena si no hay)
- `Exit` → vuelve al menú principal

### 4.3 Inventario de Llaves (`KeyInventory.cs`)

Clase estática que persiste entre salas durante una partida.

```
KeyInventory.JadeKeyId = "jade"   // ID de la Llave de Jade
KeyInventory.AddKey(id)           // Añadir llave
KeyInventory.RemoveKey(id)        // Consumir llave
KeyInventory.HasKey(id)           // Comprobar
KeyInventory.ResetAll()           // Limpiar al reintentar / volver al menú
```

También registra pickups recogidos (`MarkPickupCollected`) para evitar duplicados al revisitar salas.

---

## 5. Sistema de Combate

### 5.1 Interfaz `IDamageable`

```csharp
public interface IDamageable {
    Team DamageTeam { get; }   // Player o Enemy
    void TakeDamage(int amount, Vector2 sourcePosition);
}
```

Todos los personajes (jugador, enemigos, aliados) implementan esta interfaz. La dirección del knockback se calcula desde `sourcePosition`.

### 5.2 Ataque Cuerpo a Cuerpo (`PlayerCombat.cs`)

- Tecla: `J`
- `attackDamage` = 1, `attackCooldown` = 0.35 s
- Golpea con un overlap circular (`attackRadius` = 0.8 u) desplazado según la dirección del jugador
- Retraso de 0.08 s antes del impacto (sincronización con animación)
- Nunca golpea al propio jugador ni a aliados (`Team.Player`)

### 5.3 Arma Lanzable — Daga (`PlayerCombat.cs` + `ThrowableWeapon.cs`)

- Tecla: `K`; requiere `hasThrowable = true`
- `throwCooldown` = 0.5 s
- La daga vuela en línea recta (`speed` = 12 u/s) sin gravedad
- `lifetime` = 3 s — se destruye sola si no impacta
- Al golpear un enemigo: daño 1 y se destruye
- Al impactar una pared: animación de rotura (`DaggerShatter`)

---

## 6. Enemigos

### 6.1 Enemigo Patrullero (`EnemyPatrol.cs`)

- Camina de un lado a otro a `speed` = 2 u/s
- Gira al detectar pared o borde (con histéresis `turnCooldown` = 0.3 s)
- `maxHealth` = 3, `contactDamage` = 1 (daña al tocar)
- Al morir: puede soltar un `dropPrefab` (opcional)

### 6.2 Enemigo Perseguidor (`EnemyChaser.cs`)

| Parámetro | Valor por defecto |
|---|---|
| `maxHealth` | 3 |
| `detectRange` | 7 u |
| `loseRange` | 10 u |
| `chaseSpeed` | 3.5 u/s |
| `attackDamage` | 1 |
| `attackRange` | 1.3 u |
| `attackWindup` | 0.35 s (aviso amarillo visual) |
| `attackCooldown` | 1.2 s |
| `contactDamage` | 1 |

**Comportamiento:**
1. Idle → Detecta al jugador (dentro de `detectRange`)
2. Persigue → Para si alcanza al jugador (`stopDistance`)
3. Ataca → Se tiñe de amarillo, espera, golpea, recupera
4. Abandona la persecución si el jugador supera `loseRange`
5. No se lanza al vacío (`stopAtEdges`)
6. Al recibir daño: se pone a perseguir inmediatamente
7. Al morir: instancia una `JadeKeyPickup` (Llave de Jade)

### 6.3 Aliado (`AllyAI.cs`)

- Sigue al jugador (`followDistance` = 2.5 u, `teleportDistance` = 14 u)
- Detecta enemigos en rango (`detectRange` = 6 u) y los ataca (`attackDamage` = 1, `attackCooldown` = 0.8 s)
- Puede saltar obstáculos bajos
- Si cae demasiado lejos del jugador, se teletransporta a su lado
- `Team.Player` — no daña al jugador

---

## 7. Objetos Recogibles

### 7.1 Llave de Jade (`JadeKeyPickup.cs`)

- Cae con gravedad al suelo, luego flota y pulsa suavemente
- Al tocarla: se añade `"jade"` al `KeyInventory`
- Puede tener un `pickupId` único para recordar si ya fue recogida
- Se instancia al morir un `EnemyChaser`

### 7.2 Mejoras de Habilidad (`AbilityPickup.cs`)

Desbloquean habilidades del jugador al tocarlas:

| Enum `Ability` | Efecto |
|---|---|
| `DobleSalto` | Activa `player.hasDoubleJump` |
| `Dash` | Activa `player.hasDash` |
| `SaltoPared` | Activa `player.hasWallJump` |
| `ArmaLanzable` | Activa `combat.hasThrowable` |

Muestran un mensaje en pantalla al recogerlas. Animación de pulso y rotación.

### 7.3 Objetos de Puntuación (`PickupItem.cs`)

- Al recogerlos: añaden puntos al `ScoreManager`
- Muestran un mensaje configurable `"Has recogido {0}"`

---

## 8. Sistema de Puertas y Salas

### 8.1 Puerta Cerrada (`LockedDoor.cs`)

- Requiere `KeyInventory.JadeKeyId = "jade"` para abrirse
- Al abrir: desactiva colliders bloqueantes, muestra visual de puerta abierta
- Puede consumir la llave (`consumeKey = true` por defecto)
- El estado (abierta/cerrada) se recuerda entre salas mediante `static HashSet<string> openedDoors`
- Se resetea al reintentar o volver al menú

### 8.2 Salida de Sala (`RoomExit.cs`)

- Trigger que activa la transición al tocar el jugador
- Si hay una `LockedDoor` asociada, bloquea la salida hasta que esté abierta
- Llama a `TransitionManager.GoTo(targetScene, targetSpawnId)`

### 8.3 Gestión de Transiciones (`TransitionManager.cs`)

- Singleton persistente entre escenas
- Fade de pantalla (`fadeTime` = 0.3 s) → carga asíncrona de escena → coloca al jugador en el `SpawnPoint`
- Sistema de checkpoint de sala: `checkpointScene` / `checkpointSpawn`
- `Respawn()`: vuelve al último checkpoint de sala tras morir (alternativa cuando no hay `GameManager`)

### 8.4 Punto de Aparición (`SpawnPoint.cs`)

- Marcador en el mapa con un `id` de texto
- El `TransitionManager` busca el `SpawnPoint` con id coincidente al cargar una escena

---

## 9. Sistema de Checkpoints

```
Checkpoint (componente) → activa al tocar (o al pulsar E)
    └─ llama GameManager.SaveCheckpoint(position, player)
        └─ guarda escena, posición exacta, HP actual y HP máximo
```

Al `Retry`:
1. Carga la escena del checkpoint
2. Teletransporta al jugador a la posición guardada
3. Restaura la vida guardada
4. Resetea llaves, puertas y puntuación (`ResetRunProgress`)

---

## 10. Sistema de Gestión Global

### 10.1 `GameManager.cs`

Singleton persistente. Coordina toda la partida:
- `SaveCheckpoint()` — guarda estado del checkpoint
- `Retry()` — reinicia desde el checkpoint
- `Victory()` — muestra pantalla VICTORIA (se llama al morir el jefe final)
- `ExitToMainMenu()` — vuelve al menú y limpia el estado
- `ResetRunProgress()` — limpia `KeyInventory`, `LockedDoor`, `ScoreManager` y `MessageManager`

### 10.2 `GameOverManager.cs`

Pantalla de **DERROTA**:
- Se activa por `PlayerController.DieRoutine()` vía `ShowGameOver()`
- Congela el tiempo (`Time.timeScale = 0`)
- Botones: **Reintentar** (→ `GameManager.Retry()`) / **Salir** (→ `GameManager.ExitToMainMenu()`)
- Genera UI en runtime si no hay canvas asignado

### 10.3 `ScoreManager.cs`

- Singleton persistente (`DontDestroyOnLoad`)
- `AddPoints(int)` — acumula puntos
- `ResetAll()` — reinicia a 0
- `CurrentScore` — propiedad estática accesible desde cualquier script
- La puntuación final se muestra en la pantalla de VICTORIA

### 10.4 `MessageManager.cs`

- Singleton persistente
- Cola de mensajes: nunca pisa el mensaje actual
- Aparece en la parte inferior de la pantalla con fade-in/out
- Genera UI en runtime si no hay canvas asignado
- `Show(message, duration)` — mostrar mensaje
- `Clear()` — limpiar cola (al reintentar)

---

## 11. Pantalla de Victoria

Activada por `VictoryOnDeath.cs` al morir el **jefe final**:
- Se conecta al evento `Died` de `EnemyChaser` o `EnemyPatrol`
- Llama `GameManager.TriggerVictory()`
- Muestra: **VICTORIA**, puntuación final, botón "Volver al menú"
- Puede otorgar puntos extra al morir el jefe (`pointsOnVictory`)

---

## 12. Audio

`AudioManager.cs` — Singleton persistente con dos fuentes de audio:

| Clip | Cuando suena |
|---|---|
| `salto.wav` | Al saltar / salto de pared |
| `ataque.wav` | Al atacar (cuerpo a cuerpo y lanzable) |
| `danio.wav` | Al recibir daño |
| `muerte.wav` | Al morir |
| `cambio_escena.wav` | Al cambiar de sala |

La música de fondo (`musicaFondo`) se reproduce en bucle con `volumenMusica` = 0.5.

---

## 13. Animaciones (`PlayerAnimation.cs`)

Parámetros del `Animator` que conecta estado y animación:

| Parámetro | Tipo | Disparado cuando |
|---|---|---|
| `Speed` | Float | Velocidad de reproducción global |
| `IsWalking` | Bool | El jugador camina en el suelo |
| `Grounded` | Bool | El jugador está en el suelo |
| `VerticalSpeed` | Float | Velocidad Y del Rigidbody |
| `Dash` | Trigger | Inicia un dash |
| `Attack` | Trigger | Inicia el ataque |
| `Hurt` | Trigger | Recibe daño |
| `Dead` | Bool | El jugador muere |

Velocidades de reproducción dinámicas:
- Idle: 1×, Movimiento: 1.5×, Aire: 0.75×, Dash: 2×, Muerto: 0×

---

## 14. Fondo y Escenario

- `BackGround_Move.cs` — scroll/parallax del fondo
- `Ajustar_Fondo.cs` — ajuste de escala/posición del fondo
- Tiles de cueva en `Assets/Art/Tiles/cave/` (tileset `mainlev_build_*.asset`)
- Render: Universal Render Pipeline 2D (`Renderer2D.asset`)

---

## 15. Arquitectura de Scripts

```
Assets/Scripts/
├── BackGround/
│   ├── Ajustar_Fondo.cs         ← Ajuste visual del fondo
│   └── BackGround_Move.cs       ← Parallax / movimiento de fondo
├── Combat/
│   ├── IDamageable.cs           ← Interfaz de daño (Team + TakeDamage)
│   ├── ThrowableWeapon.cs       ← Proyectil daga (vuelo, impacto, rotura)
│   ├── DaggerShatter.cs         ← Efecto de rotura de daga
│   ├── Dagger3DVisual.cs        ← Visual 3D de la daga
│   └── DamageOnContact.cs       ← Daño genérico por contacto
├── Common/
│   ├── AudioManager.cs          ← Singleton de audio (SFX + música)
│   ├── Checkpoint.cs            ← Punto de guardado físico
│   ├── GameManager.cs           ← Gestor global (checkpoint, retry, victoria)
│   ├── GameOverManager.cs       ← Pantalla de derrota
│   ├── JadeKeyPickup.cs         ← Llave de Jade recogible
│   ├── KeyInventory.cs          ← Inventario estático de llaves
│   ├── KeyPickup.cs             ← Llave genérica (versión simple)
│   ├── LockedDoor.cs            ← Puerta que requiere llave de jade
│   ├── MessageManager.cs        ← Cola de mensajes en pantalla
│   ├── PickupItem.cs            ← Objeto recogible con puntos
│   ├── Probe2D.cs               ← Raycasts de detección (suelo, pared, borde)
│   ├── RotatingPickup.cs        ← Animación de rotación para objetos
│   ├── ScoreManager.cs          ← Puntuación global
│   └── VictoryOnDeath.cs        ← Trigger de victoria al matar al jefe
├── Enemies/
│   ├── AllyAI.cs                ← Aliado que sigue y ataca enemigos
│   ├── EnemyChaser.cs           ← Enemigo que persigue y ataca
│   └── EnemyPatrol.cs           ← Enemigo que patrulla
├── MapControllers/
│   ├── RoomExit.cs              ← Trigger de salida de sala
│   ├── SpawnPoint.cs            ← Punto de aparición del jugador
│   └── TransitionManager.cs     ← Fade + carga de escenas + respawn
├── Menu/
│   └── MenuSystem.cs            ← Lógica del menú principal
└── Player/
    ├── AbilityPickup.cs         ← Objeto que desbloquea habilidades
    ├── CameraFollowPlayer.cs    ← Cámara que sigue al jugador
    ├── HealthUI3DController.cs  ← UI de salud con corazones 3D
    ├── HealthUIController.cs    ← UI de salud 2D
    ├── PlayerAnimation.cs       ← Conexión estado → Animator
    ├── PlayerCombat.cs          ← Ataque y arma lanzable
    └── PlayerController.cs      ← Movimiento, vida, dash, salto de pared
```

---

## 16. Flujo de Juego Completo

```
Menu
 └─ Play → Room-02 (primera sala)
       ├─ Explorar y pelear
       ├─ Recoger Llave de Jade (drop de EnemyChaser o pickup en mapa)
       ├─ Abrir LockedDoor con la llave
       ├─ Cruzar RoomExit → TransitionManager → siguiente sala
       ├─ Recoger AbilityPickup → desbloquear habilidades
       ├─ Checkpoint (guarda posición, HP, escena)
       ├─ Llegar a sala del Jefe 2 (EnemyChaser/Patrol + VictoryOnDeath)
       │      ├─ Matar al jefe → VictoryOnDeath.HandleDeath()
       │      └─ GameManager.Victory() → Pantalla VICTORIA + Puntuación
       └─ Morir → GameOverManager.ShowGameOver()
              ├─ Reintentar → GameManager.Retry() (desde checkpoint)
              └─ Salir → Menu
```

---

## 17. Notas de Implementación

> [!NOTE]
> Los singletons (`GameManager`, `ScoreManager`, `AudioManager`, `MessageManager`, `TransitionManager`, `GameOverManager`) usan `DontDestroyOnLoad` para persistir entre escenas. Se auto-instancian con `[RuntimeInitializeOnLoadMethod]` si no están en la escena.

> [!IMPORTANT]
> El estado de `KeyInventory` y `LockedDoor` se resetea **completo** al reintentar o volver al menú. Los pickups con `pickupId` recordarán si ya fueron recogidos durante la misma partida.

> [!TIP]
> Para desbloquear habilidades al inicio del juego (sin pickups), simplemente establece `hasDoubleJump`, `hasDash`, `hasWallJump` a `true` en el inspector del `PlayerController`.

> [!WARNING]
> El jugador es invulnerable mientras hace dash (`invulnerableWhileDashing = true`). Ajusta esto si quieres que el dash sea más arriesgado.
