# DictionaryLookupCount: ValueRef across two runs

The step from two dictionary probes to one does not reproduce. The step from three to two does.

Same machine, same build, same benchmark. Run 1 was nine process launches with twenty iterations,
run 2 was fifteen launches with thirty. Ratios are against `TryGetValueThenIndexer`.

| Method | Run 1 mean | Run 1 ratio | Run 2 mean | Run 2 ratio |
|---|---|---|---|---|
| ContainsKeyThenIndexer | 37.11 us | 1.25 | 38.44 us | 1.29 |
| TryGetValueThenIndexer | 29.78 us | 1.00 | 29.82 us | 1.00 |
| ValueRef | 24.16 us | 0.82 | 28.14 us | 0.95 |
| AlternateLookup | 14.67 us | 0.50 | 16.05 us | 0.54 |

Every arm except `ValueRef` moved by less than 0.05 of a ratio point between the two runs.
`ValueRef` moved by 0.13, which is larger than the effect being measured. Raising the launch count
made it worse rather than better, so this is not run-to-run variance that more samples would settle.

Run 2 is the report published in `DictionaryLookupCount.md`.
