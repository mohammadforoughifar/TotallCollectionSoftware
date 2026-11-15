# Repair customer and technician creation

Scope is the shop repair module: ordinary admission, warranty and trade-in.
Customer creation reuses the existing PartyFormModal, just as sales and master
customer data do. No separate short customer form or alternative endpoint is added.

Technician creation and editing reuse TechnicianFormModal from ordinary admission,
warranty and the technician manager. It uses the existing technician service and
shared Modal/form-control/grid conventions. It does not treat a technician as a
customer or add customer-only financial fields to the technician model.

After creation, the returned ID is selected without remounting/resetting the parent
admission draft. Management editing does not change the admission's technician.
The modal edits a copy, prevents repeat saves, requires a name, and checks repair
management access; existing API authorization remains unchanged. Failed saves
keep the form open. Cancelling does not save.

Validation: 5 source tests here and 19 existing ShopWarrantyTradeInExpenses source
regressions pass. These are not Razor compilation, live permission/API integration
or browser tests. The .NET SDK is unavailable in this environment.

Other application modules and shared customer/modal implementations are unchanged.
