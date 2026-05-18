# CLAUDE.md

## Project context

Local AWS testing environment using .NET Aspire + LocalStack + Python Lambdas. 15 progressive scenarios from single-service basics (S3, SQS, DynamoDB) to multi-service pipelines (S3→SQS→Lambda→DynamoDB). .NET 10, Aspire 9.5, xUnit 2.9, LocalStack 3.8 Community, Python 3.12.

## Running tests

```bash
dotnet test scenarios/01-S3.Basic/       # single scenario
dotnet test aspire-aws.sln               # full suite (~3min, sequential)
dotnet build aspire-aws.sln              # build only
```

Docker must be running. LocalStack starts and stops automatically via Aspire.

## Architecture

**Execution flow:** `dotnet test` → xUnit loads Fixture → `LocalStackFixture.InitializeAsync()` acquires exclusive file lock on port 4566 → starts Aspire AppHost → AppHost boots LocalStack container → health check polling → `InitializeScenarioAsync()` creates scenario-specific AWS resources → tests run → `DisposeAsync()` tears down container and releases lock.

**Sequential execution is mandatory.** All scenarios share port 4566. `test.runsettings` sets `MaxCpuCount=1` so the runner executes one assembly at a time. `RunSettingsFilePath` lives in `Directory.Build.targets` (not `.props`) because `IsTestProject` is set by NuGet package imports which evaluate after `.props`.

**Fixture inheritance:** Every scenario has `Fixture : LocalStackFixture`. The base class handles Aspire lifecycle and port locking. The scenario fixture overrides `InitializeScenarioAsync()` to create its own AWS resources (buckets, queues, tables, lambdas) and exposes them as public properties.

**Lambda deployment path:** `LambdaDeployer` resolves `src/lambdas/<folder>/`, zips the Python files, calls `CreateFunctionAsync`, polls until the function reaches `Active` state.

**SNS→SQS and S3→SQS integrations** require inline IAM policy JSON on the queue (allowing the source service to `sqs:SendMessage`). See `Fixture.cs` in scenarios 04, 09, 10, 12, 13 for the pattern.

## Behavioral contract

These rules close specific failure modes observed in this project. They are not preferences.

**Think before coding.** State your interpretation of the task before writing code. If a requirement is ambiguous, ask — do not guess and proceed silently.

**Simplicity first.** Write the minimum code that satisfies the requirement. No single-use abstractions. No helper methods called from only one place.

**Surgical changes.** Touch only what the task requires. Do not refactor, rename, or "improve" adjacent code unless explicitly asked. If you spot an unrelated issue, mention it — do not fix it.

**Goal-oriented execution.** Before starting a multi-step task, state the success criteria. After each significant step, confirm it was met before continuing.

**Read before writing.** Before adding a utility or class, scan existing code for what already exists: `PollingHelper`, `AwsClientFactory`, `LambdaDeployer`, `LocalStackFixture`. Reuse first.

**Expose conflicts, don't average them.** If two contradictory patterns exist in the codebase, pick one, implement it, and flag the inconsistency explicitly. Never silently blend both.

**Tests verify intent.** Tests must assert business behavior ("message was processed and persisted"), not implementation details. Describe *why* the behavior is expected, not just *what* the assertion checks.

**Checkpoints on complex tasks.** After each meaningful step in a multi-step task, summarize what was done and what comes next before continuing.

**Convention over novelty.** Follow the style already established in existing scenarios. Naming, structure, and patterns in the codebase are the standard — match them even if you would choose differently.

**Fail visibly.** If a step fails, is uncertain, or was skipped, say so explicitly. Never report success while hiding a partial failure.

## Non-negotiable conventions

**One project per scenario.** Never consolidate scenarios into a single test project. Each scenario must be independently runnable via `dotnet test scenarios/<name>/`.

**Lambda handlers are Python, tests are C#.** Tests deploy and invoke Lambdas via AWS SDK; they do not import or reference Python code directly.

**No fixed `Task.Delay` in tests.** Use `PollingHelper.WaitUntilAsync` with timeout for async assertions. Use `PollingHelper.AssertNeverAsync` for negative assertions. Fixed sleeps are flaky.

**AWS clients always via `AwsClientFactory`.** Never construct `AmazonXxxClient` directly in test code.

**`LocalStackFixture` stays generic.** Scenario-specific resource setup belongs in the scenario's own `Fixture.cs`, not in `LocalStackFixture`.

**Lambda handlers read endpoint from env.** Always use `os.environ.get("AWS_ENDPOINT_URL")` in Python — LocalStack injects it automatically.

## Environment limitations

- **macOS ARM64:** Lambda scenarios (07, 08, 11, 12, 13, 14) are skipped via `[SkipOnMacOsArm64LocalStackLambdaFact]` because LocalStack 3.8 Lambda invocation is unreliable on this platform.
- **StepFunctions (scenario 15):** Tests use `[Fact(Skip = ...)]` because Step Functions is unreliable in LocalStack Community edition.
- **CI without Docker-in-Docker:** Set `LAMBDA_EXECUTOR=local` in the LocalStack container environment.
