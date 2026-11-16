# Work-order SMS appearance tests

`dotnet run --project tests/WorkOrderSmsUiOffline` exercises the production
work-order SMS status mapping, bounded/cancellable status-read polling and the shared
DTO. It covers all persisted states, accepted versus delivered, ambiguous outcomes,
safe failure-code display, cancellation and replacement of stale detail watches.
Polling only reads status; it never invokes the SMS provider.

This is an offline logic test. It does not render Razor, start the application,
contact Kavenegar or send SMS. Source-level markup checks are in
`tests/WorkOrderSms/test_work_order_sms.py`; extracted component/API logic with fakes
is exercised by `tests/WorkOrderSmsUiHarness/run.py`.
