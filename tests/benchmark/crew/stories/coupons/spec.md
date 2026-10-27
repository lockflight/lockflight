# Feature Specification: Coupon codes

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Redeem a coupon (Priority: P1)

A customer enters a coupon code at checkout and sees the reduced subtotal.

1. **Given** a 80.00 subtotal, **When** the code `SAVE10` is applied, **Then** the subtotal is 72.00
2. **Given** a 3.00 subtotal, **When** the code `FLAT5` is applied, **Then** the subtotal is 0.00, never negative
3. **Given** the code `save10` in lower case, **When** it is applied to 80.00, **Then** the subtotal is 72.00
4. **Given** the code `BOGUS`, **When** it is applied, **Then** the request is rejected as an unknown coupon

## Requirements *(mandatory)*

- **FR-001**: `SAVE10` MUST take 10 percent off the subtotal, rounded to cents (half to even)
- **FR-002**: `FLAT5` MUST take 5.00 off the subtotal, but never below 0.00
- **FR-003**: Coupon codes MUST be matched without regard to case
- **FR-004**: An unknown code MUST be rejected with an UnknownCouponException that carries the code
