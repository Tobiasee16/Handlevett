# Handlevett

A Norwegian grocery-aware meal planner. It ranks dinners by budget, calories, preferences,
estimated shopping cost, live discounts and nutrition — using real prices from
[Kassal.app](https://kassal.app) and recipes generated locally by [Ollama](https://ollama.com).

*Handlevett* is Norwegian for "shopping sense". The interface is in Norwegian; the code and
documentation are in English.

> **Status:** a working personal project. It runs, it has tests, and the schema is managed by
> migrations — but there is no authentication and no deployment story yet.

---

## What it does

- Pulls current prices for ~20 staple ingredients across Norwegian grocery chains
- Scores every recipe on budget fit, calorie target, active discounts, protein and your preferences
- Explains each ranking in plain language rather than just showing a number
- Generates new recipes from whatever is actually cheap right now, using a local LLM
- Lets you pick specific products and build a meal from exactly those

---

## Architecture

The database is the source of truth. **Nothing on the request path calls Kassal.app or Ollama** —
a page load is one indexed SQLite query.

```
                     ┌─── background ─────────────────────────────┐
Kassal.app  ────────►│ IngestBackgroundService                    │
                     │   upsert Products, append ProductPrices    │
                     │   (only when a price actually changed)     │
Ollama      ────────►│ OllamaRecipeGenerator → Recipes            │
                     └────────────────┬───────────────────────────┘
                                      │
                                      ▼
                              SQLite (11 tables)
                                      │
                                      ▼
                     Razor Pages ── read-only, ~5 ms
```

| | |
|---|---|
| Page load | ~5 ms |
| Restart to ready | ~1 s (skipped if the catalogue is fresh) |
| Cold ingest | ~30 s for 21 ingredient searches |

The ingest job runs at startup and every 6 hours. If prices were fetched within the last hour it
skips the network entirely, which is what keeps `dotnet watch` usable.

---

## Running it

```bash
dotnet watch run
```

The app serves on `http://localhost:5286`. Migrations and reference data are applied automatically
on first run — there is no setup step.

Without a Kassal.app key the app starts normally but the catalogue stays empty, and the UI says so.

### Tests

```bash
dotnet test
```

50 tests covering the scoring formula, the package-weight parser and the ingredient-key resolver —
the three places where a bug produces a plausible wrong answer instead of an exception.

---

## Configuration

Secrets belong in .NET user-secrets, never in `appsettings.json`.

### Live prices

```bash
dotnet user-secrets set "Kassalapp:ApiKey" "YOUR_API_KEY"
dotnet user-secrets set "Kassalapp:UseLivePrices" "true"
```

Get a free key from [kassal.app/api](https://kassal.app/api). The free tier allows 60 calls per
minute; the ingest job paces itself accordingly.

### AI-generated recipes

```bash
ollama pull llama3.2
dotnet user-secrets set "Ollama:Enabled" "true"
```

Ollama must be reachable at `http://localhost:11434/`. The model is constrained to the ingredient
keys currently in the catalogue and asked to write in Norwegian. Generated recipes are stored as
ordinary rows and reused for 8 hours. If Ollama is slow or unavailable the app falls back to the
curated recipes and says so in the UI.

> First generation on modest hardware can take a minute or more. The page shows a live progress
> indicator with an elapsed counter rather than appearing to hang.

### Settings reference

| Section | Controls |
|---|---|
| `Kassalapp` | API key, base URL, timeout, live-price toggle |
| `Ollama` | Model, timeout, recipe count, enabled toggle |
| `Ingest` | Run interval, delay between searches, startup freshness window |
| `Scoring` | Ranking weights — budget, calories, discounts, protein, over-budget penalty |

Every scoring weight is configurable. To make price dominate the ranking:

```json
{ "Scoring": { "Budget": 60, "Calories": 10 } }
```

---

## Database

SQLite at `Data/handlevett.db`, managed by EF Core migrations. The schema is provider-agnostic, so
moving to PostgreSQL is a package reference and a connection string.

```bash
dotnet ef migrations add <Name> --output-dir Data/Migrations
```

| Group | Tables |
|---|---|
| Reference data | `Stores`, `Ingredients`, `IngredientAliases` |
| Catalogue & price history | `Products`, `ProductNutrition`, `ProductPrices` |
| Recipes | `Recipes`, `RecipeIngredients`, `RecipeSteps`, `RecipeTags` |
| Operations | `IngestRuns` |

Two design notes:

**Prices are appended on change, not on a schedule.** `ProductPrices` gets a row only when a price
actually differs from the last observation. A daily snapshot of every product would store the same
value over and over.

**`Products` carries a current-price projection.** SQLite has no materialised views, so
`CurrentPrice` and `CurrentDiscountPercent` are columns maintained by the ingest job. The read path
avoids a correlated subquery per row, while `ProductPrices` keeps the full history.

---

## Project layout

```
Data/            EF Core entities, DbContext, migrations
Models/          Domain records (GroceryProduct, MealRecipe, MealRecommendation)
Pages/           Razor Pages — planner and product browser
Services/        Ingest, scoring, recipe generation, formatting
wwwroot/css/     One hand-written stylesheet, design tokens, no framework
tests/           xUnit
```

There is no Bootstrap, no Tailwind, no jQuery and no npm. The stylesheet is a single tokenised
file with light and dark themes.

---

## Roadmap

1. **Ingredient normalisation** — match store product names to canonical ingredients automatically.
   `IngredientAliases` exists; the matching is still seeded by hand. This is the main reason to move
   to PostgreSQL, whose `pg_trgm` trigram indexes solve it directly.
2. **Weekly plan** — pick 5–7 dinners that maximise ingredient overlap to cut waste and cost.
3. **Price sparklines** — use `ProductPrices` to show whether something is cheaper than usual.
4. **Shopping list** — a combined, checkable list for the chosen meals.
5. **Pantry** — subtract what you already own, so the cost estimate matches the receipt.

---

## License

[MIT](LICENSE)

Price data comes from Kassal.app and is subject to their terms. Cost figures are estimates — check
the shelf price before you shop.
