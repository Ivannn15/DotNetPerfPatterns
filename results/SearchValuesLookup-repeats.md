# SearchValuesLookup: the 4096 row across three runs

The 4096 payload does not reproduce. The three smaller sizes do. This file records what each run
measured, because the section in the README makes a claim about which rows to trust.

Same machine, same build, same benchmark. Run 1 was three process launches at two payload sizes.
Runs 2 and 3 were nine launches, twenty iterations, at four sizes. Run 2 shared the machine with
other work; run 3 had it to itself.

Gap is `CachedArray` mean minus `Cached` mean, in nanoseconds.

| Scan length | Run 1 | Run 2 | Run 3 |
|---|---|---|---|
| 126 | 9.21 | 8.46 | 8.11 |
| 510 | not measured | 7.96 | 8.04 |
| 1022 | not measured | 7.68 | 7.16 |
| 4094 | 0.14 | 34.83 | 4.97 |

The three shorter scans hold at roughly 8 ns in every run. The longest one gave a gap of nothing,
then four times too much, then half. Its standard deviation in run 3 is 10.2 ns on the baseline
against 2.3 ns for the other two rows at the same size, which is larger than the effect being
measured.

Run 3 is the report published in `SearchValuesLookup.md`. Its 4096 row is included there for
completeness and should not be read as a measurement of anything.
