# Shop invoice compact buttons

Source regressions:

```sh
python -B -m unittest discover -s TotallCollectionSoftware/inventory/tests/ShopHistoryButton
```

Chromium fixture (not a Blazor/API integration):

1. Run `generate_fixture.py`; output is outside the repository by default, or set `FIXTURE_DIR`.
2. Serve that directory using an HTTP server.
3. Install `puppeteer-core` outside the repository and supply `NODE_PATH` and `CHROME_BIN`.
4. Run `node layout.cjs` with `FIXTURE_URL` pointing to the served `row.html`.

The fixture uses production Bootstrap, app and OrderForm CSS, the real Persian font,
normalized sale column weights, and representative controls in both invoice steps.
At widths 320, 360, 390, 600, 768, 992, 1280 and 1568 it checks page overflow,
F8/delete alignment, unbroken purpose/shortcut labels, and compact footer geometry.
This does not compile Razor or verify save, keyboard events, permissions or API calls.
No shared modal or other-module styles were changed.
