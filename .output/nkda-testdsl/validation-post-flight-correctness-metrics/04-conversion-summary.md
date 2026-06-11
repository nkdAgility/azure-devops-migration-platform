# Conversion Summary

| Scenario | Test Method | Result |
|---|---|---|
| Matching revision counts produce zero missing and zero delta | `PostFlightValidation_MatchingRevisionCounts_ProducesZeroMissingAndZeroDelta` | PASS |
| Fewer target revisions increment the missing counter | `PostFlightValidation_FewerTargetRevisions_IncrementsRevisionsMissingCounter` | PASS |
| Broken links are detected and counted | `PostFlightValidation_BrokenLinks_AreDetectedAndCounted` | PASS |
| Sample rate zero skips all correctness checks | `PostFlightValidation_SampleRateZero_EmitsNoCorrectnessMetrics` | PASS |

All 4 scenarios converted and passing.
