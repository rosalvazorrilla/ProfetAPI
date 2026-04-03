# ProfetAPI — Claude Context

## Descripcion del Proyecto
Migracion de un CRM B2B SaaS multi-tenant de .NET 4 a .NET 9. Backend REST API para gestion comercial (leads, oportunidades, pipelines) con scoring de prospectos, omnicanalidad y automatizacion. La migracion va modulo por modulo manteniendo compatibilidad con la estructura existente.

## Stack Tecnico
- **Framework:** ASP.NET Core 9.0 (C#)
- **ORM:** Entity Framework Core (mezcla Code-First y Database-First)
- **Auth:** ASP.NET Identity + JWT Bearer (HMAC SHA256, 8h de validez)
- **DB:** Azure SQL (SQL Server) — esquema altamente relacional y normalizado
- **Docs:** Swagger/OpenAPI con comentarios XML + Postman-importable (siempre visible)
- **Host:** Azure (profetapi.azurewebsites.net) | Dev: MAMP macOS puerto 5051/7277
- **Frontend esperado:** Angular en localhost:4200

## Jerarquia Multi-Tenant (CRITICO)
```
Admin Global (SaaS)
  └── Customer (empresa suscriptora) → tiene Subscription + SetupToken
        └── Account (entorno operativo, ej: "Ventas", "Proveedores")
              ├── Users / Teams (con roles granulares)
              ├── Funnels / Stages (clonados desde FunnelTemplate)
              ├── Leads / Deals / Contacts / Companies
              ├── ScoringModel propio (clonado desde ScoringTemplate)
              ├── ItemCatalogs: "Motivos de Perdida" y "Tipos Oportunidad"
              ├── CustomFieldDefinitions activadas (AccountCustomField)
              └── Tags propias

```

## Catalogo Global vs Catalogo de Tenant

### Admin Global crea (seed data del sistema):
| Tabla | Descripcion |
|---|---|
| `Industry` | Sectores de la industria |
| `FunnelTemplate` + `FunnelTemplateStage` | Plantillas de embudos base |
| `ScoringTemplate` + `ScoringTemplateQuestion` + `ScoringTemplateAnswerOption` | Plantillas de calificacion |
| `TemplateCategory` | Categorias para organizar ScoringTemplates |
| `CustomFieldDefinition` | Pool global de "Variables" (campos capturables en leads) |
| `ProspectSource` | Fuentes de prospectos globales |
| `LeadTier` | Niveles de calificacion (Estandar, Premier, etc.) |

### El Tenant personaliza (resultado del Setup Wizard):
| Tabla | Descripcion | Origen |
|---|---|---|
| `Funnel` + `Stage` | Embudo propio | Clonado de FunnelTemplate |
| `ScoringModel` + `ScoringQuestion` + `ScoringAnswerOption` + `ScoringRule` | Scoring propio | Clonado de ScoringTemplate |
| `AccountCustomField` | Variables activas (llave compuesta AccountId+FieldId) | Seleccion del pool global |
| `ItemCatalog` (Motivos de Perdida) | Account.LeadLostReasonsPackagesId → ItemCatalog.CatalogId | Creado en wizard |
| `ItemCatalog` (Tipos Oportunidad) | Account.LeadDealsTypesPackagesId → ItemCatalog.CatalogId | Creado en wizard |
| `Tag` | Etiquetas propias (Tag.CustomerId = tenant) | Creado en wizard |

## Variables — Contexto de Migracion
En el sistema viejo (`.NET 4`), las variables eran un string hardcodeado en el Campaign:
```
"id,leadDate,leadScore,name,email,phone,company,city,messageSent,comments,
 prospectSource,contactForm,budget,buyingDecision,needDegree,buyingTime,
 position,companyType,status,contact,qualityScore,engagement,indistrySector"
```
En el sistema nuevo, el Admin Global pre-carga `CustomFieldDefinition` con todos los campos posibles (con `FieldCode` y `FieldName`). Cada Account activa los que necesita via `AccountCustomField` (llave compuesta AccountId + FieldId + IsVisibleOnCard).

## Scoring — Contexto de Migracion
En el sistema viejo, `SetScoreQuestions(campaign)` inicializaba el scoring al crear una campaña con valores fijos. En el sistema nuevo:
- Admin Global crea `ScoringTemplate` con `ScoringTemplateQuestion` y `ScoringTemplateAnswerOption`
- El Wizard clona la plantilla a `ScoringModel` + `ScoringQuestion` + `ScoringAnswerOption` + `ScoringRule`
- Las reglas definen puntos por respuesta (`ActionType: "ADD_POINTS"`, `ActionValue: "50"`)
- Los indicadores de la imagen (Presupuesto 15%, Decision de compra 15%, etc.) se implementan como `ScoringQuestion` con peso relativo

## Catalogos de Items por Account
`ItemCatalog` es el contenedor generico con `AccountId` y `Name`. `CatalogItem` son los elementos.
- Al crear una Account, se crean automaticamente 2 `ItemCatalog`:
  - "Motivos de Perdida" → su `CatalogId` se guarda en `Account.LeadLostReasonsPackagesId`
  - "Tipos de Oportunidad" → su `CatalogId` se guarda en `Account.LeadDealsTypesPackagesId`

## Estado Actual del Desarrollo
| Modulo | Estado |
|---|---|
| Modelos (~69 entidades) | Completo |
| AuthController (login JWT) | Completo |
| CustomerController (CRUD admin global) | Completo |
| UserController (create-global-admin) | Completo |
| PlansController (planes + features + addons) | Completo |
| **Catalogos Admin Global (Fase 1)** | **SIGUIENTE** |
| **Setup Wizard (Fase 2)** | **PENDIENTE** |
| CRM core (Leads, Deals, etc.) | Pendiente |
| Scoring con IA (Fase 3) | Largo plazo |

## Plan de Accion

### FASE 1 — Catalogos del Admin Global
CRUDs con DTOs y Swagger annotations (para exportar a Postman):

| Controller | Tablas involucradas |
|---|---|
| `IndustriesController` | `Industry` |
| `FunnelTemplatesController` | `FunnelTemplate` + `FunnelTemplateStage` |
| `ScoringTemplatesController` | `ScoringTemplate` + `ScoringTemplateQuestion` + `ScoringTemplateAnswerOption` + `TemplateCategory` |
| `CustomFieldDefinitionsController` | `CustomFieldDefinition` (el pool de Variables) |
| `ProspectSourcesController` | `ProspectSource` |
| `LeadTiersController` | `LeadTier` |

### FASE 2 — Setup Wizard (SetupController)
Flujo stateful via `Customer.SetupStep`, todo transaccional:

```
GET  /api/setup/status?token=...           → valida token, retorna paso actual
POST /api/setup/step1/admin                → crea ApplicationUser genesis del tenant
POST /api/setup/step2/account              → crea Account, asigna IndustryId
POST /api/setup/step3/variables            → activa CustomFieldDefinitions → AccountCustomField[]
POST /api/setup/step4/funnel               → clona FunnelTemplate → Funnel + Stages
POST /api/setup/step5/scoring              → clona ScoringTemplate → ScoringModel + Questions + Rules
POST /api/setup/step6/catalogs             → crea ItemCatalogs (motivos de perdida, tipos oportunidad) + Tags iniciales
POST /api/setup/complete                   → Status = "Activo", limpia SetupToken
```

El wizard es re-entrable: el cliente puede pausar y volver, el progreso se guarda en `Customer.SetupStep`.

### FASE 3 — Scoring con IA
- API para calificar leads automaticamente basado en `ScoringModel` de la Account
- Integracion con IA para scoring dinamico por respuestas
- Tablas listas: `ScoringModel`, `ScoringQuestion`, `ScoringRule`, `LeadTier`, `LeadScoringAnswer`

## Convenciones Criticas
- **Soft delete:** flags `Active` y `Deleted` — nunca borrar fisicamente
- **Migraciones:** SIEMPRE manuales via `Querys.sql` (NUNCA usar `dotnet ef migrations add`)
- **Multi-tenancy:** `CustomerId` en `ApplicationUser` es la llave del tenant
- **Swagger:** documentar SIEMPRE con `[SwaggerOperation]`, `[SwaggerResponse]`, `[ProducesResponseType]` para exportar a Postman
- **SetupToken:** Guid unico por Customer, validar en cada step, considerar agregar `SetupTokenExpiry`
- **Transaccionalidad:** Setup Wizard usa `IDbContextTransaction` de EF Core
- **Clonado de plantillas:** siempre registrar el `OriginatingTemplateId` en la entidad clonada (ya existe en `Funnel`)

## Estructura de Directorios
```
ProfetAPI/
├── Controllers/
│   ├── AuthController.cs          (completo)
│   ├── CustomerController.cs      (completo)
│   ├── UserController.cs          (completo)
│   └── [proximos controllers aqui]
├── Models/                        (~69 entidades, completo)
├── Dtos/                          (organizar por dominio: /Admin, /Setup, /CRM)
├── Data/
│   └── ApplicationDBContext.cs
├── Program.cs
├── appsettings.json               (JWT secret + conexion Azure SQL)
└── Querys.sql                     (migraciones manuales)
```

## Notas de Seguridad
- JWT secret hardcodeado en appsettings.json — mover a Azure Key Vault en produccion
- CORS abierto (`AllowAnyOrigin`) — restringir antes de produccion
- `SetupToken` deberia tener fecha de expiracion (campo `SetupTokenExpiry` pendiente de agregar)
