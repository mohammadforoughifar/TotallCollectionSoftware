# Kavenegar SDK gateway tests

`dotnet run --project tests/KavenegarSms` restores Kavenegar 2.0.1 and tests the
production gateway and settings using an in-memory `HttpMessageHandler`.
It does **not** contact Kavenegar or send SMS. It checks disabled/unconfigured
and invalid requests, a single POST, provider and message states, malformed
responses, timeouts/network failures, credential redaction and no retries.
A normal .NET 8 SDK and NuGet access are required.

`python3 -m unittest discover -s tests/KavenegarSms -p 'test_*.py'` checks
source/configuration invariants only. It is not a substitute for the C# test,
restoring the real package or building the application.
