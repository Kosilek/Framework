# Framework

**v0.1.0** | 2026-02-19

---

## Changelog

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
