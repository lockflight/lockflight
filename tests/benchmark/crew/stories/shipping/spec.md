# Feature Specification: Shipping cost

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Price shipping by weight (Priority: P1)

A customer sees the shipping cost of an order before paying.

1. **Given** a 1 kg parcel and a 10.00 order, **When** shipping is priced, **Then** it costs 5.00
2. **Given** a 2.1 kg parcel and a 10.00 order, **When** shipping is priced, **Then** it costs 10.00
3. **Given** an order of exactly 50.00, **When** shipping is priced, **Then** it is free
4. **Given** a parcel of 0 kg or less, **When** shipping is priced, **Then** the request is rejected

## Requirements *(mandatory)*

- **FR-001**: System MUST charge 5.00 for the first kilogram or part of it
- **FR-002**: System MUST charge 2.50 for every further started kilogram
- **FR-003**: Orders of 50.00 or more MUST ship free
- **FR-004**: System MUST reject a weight of zero or less with an ArgumentOutOfRangeException
