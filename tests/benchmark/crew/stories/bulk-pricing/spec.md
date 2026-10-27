# Feature Specification: Bulk pricing

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Quote a bulk order (Priority: P1)

A buyer asks for a quote and sees the unit price, the discount tier and the total.

1. **Given** a list price of 4.00 and 9 units, **When** a quote is made, **Then** the unit price is 4.00, the discount 0 percent and the total 36.00
2. **Given** a list price of 4.00 and 10 units, **When** a quote is made, **Then** the unit price is 3.80, the discount 5 percent and the total 38.00
3. **Given** a list price of 0.99 and 50 units, **When** a quote is made, **Then** the unit price is 0.89, the discount 10 percent and the total 44.50
4. **Given** 0 units, **When** a quote is made, **Then** the request is rejected

## Requirements *(mandatory)*

- **FR-001**: 1 to 9 units MUST be sold at the list price
- **FR-002**: 10 to 49 units MUST get 5 percent off the unit price; 50 units or more MUST get 10 percent off
- **FR-003**: The discounted unit price MUST be rounded to cents (half to even) before the total is computed
- **FR-004**: A quantity of zero or less MUST be rejected with an ArgumentOutOfRangeException
