# Feature Specification: Discount rules

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Safe discounts (Priority: P1)

Pricing applies percentage discounts safely to any amount.

1. **Given** an amount of 80.00, **When** a 25 percent discount is applied, **Then** it is 60.00
2. **Given** an amount of -10.00, **When** any discount is applied, **Then** the request is rejected
3. **Given** an amount of 19.99, **When** a 100 percent discount is applied, **Then** it is 0.00
4. **Given** an amount of 10.00, **When** a discount with more than two decimals (12.345 percent) is applied, **Then** the request is rejected

## Requirements *(mandatory)*

- **FR-001**: `ApplyDiscount` MUST take the given percentage off the amount, rounded to cents (half to even)
- **FR-002**: `ApplyDiscount` MUST reject a negative amount with an ArgumentOutOfRangeException
- **FR-003**: `ApplyDiscount` MUST reject a percentage with more than two decimals with an ArgumentException
