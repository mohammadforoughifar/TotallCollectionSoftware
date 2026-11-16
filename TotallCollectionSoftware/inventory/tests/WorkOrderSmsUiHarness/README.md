# Work-order SMS details and owner endpoint

`python tests/WorkOrderSmsUiHarness/run.py` extracts the current SMS status session,
refresh, cancellation and close methods from `WorkOrders.razor` and the owner-only
`sms-events` action from `WorkOrdersController.cs`, then runs them with a .NET 8 SDK.
It uses fake HTTP, a controlled timer and fake LINQ/EF: no API host, database or SMS
provider is contacted. The returned fire-and-forget watch task is retained by the
fixture solely so cancellation and late-response tests can await it deterministically.

The tests cover pending to simulated 427 and accepted results; owner and company guards;
failure-code projection; non-owner visibility; manual refresh; read errors; cancellation
and stale responses after switching, closing, reopening or disposing the details. They
cannot prove real EF translation/tenant filters, the Razor render, the published app,
or delivery. The companion source-level markup checks are in `tests/WorkOrderSms`.
