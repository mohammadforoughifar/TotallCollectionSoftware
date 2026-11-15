# Dispatch preview persistence audit

Scope: nested shop application (`TotallCollectionSoftware/inventory`), not the
older sibling application. This is a source audit, NOT a database round-trip test.

## Existing persisted data path

- Form: `Inventory.Client/Pages/Catalog/GoodsIssues.razor`
- Command/read DTO: `Inventory.Shared/Dtos/GoodsIssueDtos.cs`
- Service: `Inventory.Api/Services/Catalog/GoodsIssueService.cs`
- Entities: `Inventory.Api/Entities/Catalog/GoodsIssue.cs`
- Schema: `Inventory.Api/Data/GoodsIssueSchemaV1.cs`

`SaveAsync` validates the customer and line quantities/names, generates the number
on creation, copies date/party/name/description and line product names, quantities
and notes into entities, calls `SaveChangesAsync`, then reloads and returns a DTO.
Party name is snapshotted from the selected party. Number is generated, not input.

| Preview field | Current persistence support |
| --- | --- |
| Dispatch number/date | GoodsIssue.Number/Date; number generated on creation |
| Recipient name | GoodsIssue.PartyId/PartyName; name snapshot at save |
| Item name/quantity | GoodsIssueLine.ProductName/Quantity |
| Line count/quantity sum | Derived from saved lines; no extra storage needed |
| Document explanation | GoodsIssue.Description |
| Per-line remarks | Entity/command/DTO/service support Note; current item form does not expose a dedicated Note input |
| Recipient phone/address/identity | Not returned by GoodsIssueDto or snapshotted on GoodsIssue; requires authorized linked-party enrichment or explicit snapshot design |
| Linked invoice | No invoice relation/number in GoodsIssueCommand/Dto/entity |
| Warehouse | No warehouse field/relation in GoodsIssueCommand/Dto/entity |
| Carrier/vehicle/delivery address | No fields in form/command/DTO/entity |
| Unit | No unit in GoodsIssueLineDto/entity; current dispatch is quantity-only |
| Signatures | Printed blank spaces, not saved digital signatures |

## Important semantic mismatch

The existing GoodsIssue entity explicitly documents that it does NOT change
inventory, cost or accounting. The static prototype's “outgoing warehouse” wording
must not be treated as a verified stock movement. Connecting to an actual stock
transaction is a separate business decision, not a print-only change.

## Status / integration gate

The prototype JavaScript data is illustrative and NOT saved. Existing goods-issue
records do persist their supported fields, but the new preview is not wired to them.
Do not claim full persistence. Before production integration, either remove unsupported
print fields or approve shop-scoped form/command/entity/schema/service additions,
including create/edit/read/print round-trip tests and tenant isolation. Do not put
invoice-linked recipient data in manual print settings. No schema was changed in
this audit. Compilation/DB validation cannot be claimed: no .NET SDK is installed.
