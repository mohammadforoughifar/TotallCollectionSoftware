# Invoice print paper chooser

The main invoice footer has no paper selector. Clicking save-and-print (or F4)
opens a small child modal offering only A4 and A5, with A4 initially selected.
Cancel/Escape closes the choice without saving. Confirmation closes the dialog
and uses the existing validated save-and-print path. Ordinary save is unchanged.
The child marker prevents invoice keyboard navigation from leaking through it.

`python -B -m unittest discover -s TotallCollectionSoftware/inventory/tests/ShopPrintPaperChooser`

These are source regression checks, not Razor compilation or live API/printer tests.
