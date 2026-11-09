<!-- lockflight:ci:v1 head=d70b903c0a9191107a89042c38b3a5e37fec2593 -->
![FAIL](https://raw.githubusercontent.com/lockflight/lockflight/v0.3.0/docs/assets/badges/verdict-fail.png) **Lockflight: FAIL** · `001-shipping` · class **fix** · head `d70b903` · base `1d614d9`

Status: `gates` FAIL · `lockflight/judge` PASS

> **Next:** Gates (pr): policy.protected PASS · format PASS · quality.suppressions FAIL

<details open><summary>Spec changes (30 lines): specs/001-shipping/spec.md</summary>

```diff
diff --git a/specs/001-shipping/spec.md b/specs/001-shipping/spec.md
new file mode 100644
index 0000000..1f3bc37
--- /dev/null
+++ b/specs/001-shipping/spec.md
@@ -0,0 +1,24 @@
+# Feature Specification: Shipping
+
+**Feature**: `001-shipping`
+**Status**: Draft
+
+## User Scenarios & Testing
+
+### User Story 1 - Pay for shipping (Priority: P1)
+
+A shopper sees the shipping cost of a parcel.
+
+**Acceptance Scenarios**:
+
+1. **Given** a parcel of 1 kg, **When** shipping is priced, **Then** it costs 5.00
+
+## Requirements
+
+### Functional Requirements
+
+- **FR-001**: System MUST charge 5.00 for the first kilogram
+
+## Success Criteria
+
+- **SC-001**: Every scenario above is covered by a test that failed before the code existed.
```

</details>

| Check | Verdict | | Detail |
|---|---|---|---|
| Red at red commits | PASS | required | 1 cycles, 1 tests, each red in production code |
| Tests since red | PASS | required | 1 unchanged, 0 amends accepted |
| Existing tests changed | PASS | advisory | no existing test changed |
| Test host tampering | PASS | required | no code in the test assemblies that could tamper with the test host |
| Green at head | PASS | required | 5 tests, 5 test cases run, 0 failed |
| Trace | PASS | required | 1/1 requirements of 001-shipping |
| Gates (pr) | FAIL | required | policy.protected PASS · format PASS · quality.suppressions FAIL |
| Review | — | off | no review ran (no API key configured, a fork, or the review job was skipped) |
| Approvals | — | off | off for this change class |
| Change class | PASS | required | class fix (routing) |
| Policy vs base | PASS | required | lockflight.yaml is no looser than the base's |

<details><summary>Red/green cycles</summary>

| Cycle | Red commit | Test | Red | Since red | At head |
|---|---|---|---|---|---|
| 1 | `a5922ca` | `Shop.Tests:Shop.Tests.PriceCalculatorTests.TheFirstKilogramCostsFive` | RED\_STUB | unchanged | pass |

</details>
<details><summary>Trace</summary>

| Requirement | Tests (red → green) | Status |
|---|---|---|
| FR-001 | `PriceCalculatorTests.TheFirstKilogramCostsFive` | covered |

</details>

<sub>Evidence: the `lockflight-verdict` artifact · engine `0.3.0` · policy from `1d614d9` · this comment says what the CI re-ran; it does not show the tests were written first.</sub>
