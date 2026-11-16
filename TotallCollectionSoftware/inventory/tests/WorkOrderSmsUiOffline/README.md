# Work-order SMS appearance tests

`dotnet run --project tests/WorkOrderSmsUiOffline` exercises the production
work-order SMS status mapping and the shared DTO. It covers all persisted states,
accepted versus delivered, ambiguous outcomes and safe failure-code display.

This is an offline logic test. It does not render Razor, start the application,
contact Kavenegar or send SMS. Source-level markup checks are in
`tests/WorkOrderSms/test_work_order_sms.py`.
