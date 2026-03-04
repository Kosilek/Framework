# Framework

**v0.2.0** | 2026-03-04

---

## Changelog

### v0.2.0 (2026-03-04)
- **FM.General** — базовая сборка: Bootstrap, IBootstrap, IRegisterInDI, IRegistrationContext. Все модули зависят от General
- **FM.General** — base assembly: Bootstrap, IBootstrap, IRegisterInDI, IRegistrationContext. All modules depend on General
- **Bootstrap** — discovery IBootstrap через Reflection, DiBootstrap запускается первым
- **Bootstrap** — IBootstrap discovery via Reflection, DiBootstrap runs first
- **DI** — DiBootstrap, IRegisterInDI discovery, RegistrationContext. Модули регистрируются без зависимости от _FM.DI
- **DI** — DiBootstrap, IRegisterInDI discovery, RegistrationContext. Modules register without _FM.DI dependency
- **IDIManager** — RegisterSingleton, RegisterTransient, Resolve, TryResolve
- **Logger** — LoggerDiRegistration (IRegisterInDI), LogCaptured event для Dev Console
- **Logger** — LoggerDiRegistration (IRegisterInDI), LogCaptured event for Dev Console
- Удалён _FM.Appboot, функционал перенесён в Bootstrap
- Removed _FM.Appboot, functionality moved to Bootstrap
- Editor: SymbolsForGroup (Add/Remove/Clear define symbols), пресеты DEV_CONSOLE, DEBUG

### v0.1.0 (2026-02-19)
- Интегрирована проверенная DI система (**FM.DI**): контейнер с поддержкой Singleton/Transient, constructor injection, именованные регистрации, атрибуты `[Inject]`, `[InjectConstructor]`, `[InjectMethod]`, `UnityInjectService` для Unity-компонентов
- Integration of verified DI system (**FM.DI**): container with Singleton/Transient, constructor injection, named registrations, inject attributes, UnityInjectService for Unity components

### v0.0.1 (2025-02-19)
- Добавлены правила проекта `.cursorrules`, конфигурация `.editorconfig`
- Добавлена структура `Assets/_Framework/` с модулями Runtime.Pure и Runtime.Unity
- Добавлен assembly definition `_FM.DI` (Dependency Injection)
- Добавлен workspace `_framework.code-workspace`

---

## Описание / Description

**RU:** Unity Framework для мобильных платформ. URP, оптимизация под mobile. Модульная архитектура с разделением на pure C# (Runtime.Pure) и Unity-specific код (Runtime.Unity). Интегрирована проверенная DI система **FM.DI** — внедрение зависимостей через конструктор и поля/свойства с атрибутами.

**EN:** Unity Framework for mobile platforms. URP, optimized for mobile. Modular architecture with separation into pure C# (Runtime.Pure) and Unity-specific code (Runtime.Unity). Integrated verified DI system **FM.DI** — dependency injection via constructor and field/property attributes.
