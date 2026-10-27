# Feature Specification: Loyalty points

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Award points for a purchase (Priority: P1)

A customer earns loyalty points for every purchase; points on a weekend count double, and the points a customer holds
decide their tier.

1. **Given** a customer with 0 points, **When** a purchase of 45.50 on Wednesday 2026-10-21 is awarded, **Then** the award is 45 points and the tier is Bronze
2. **Given** a customer with 995 points, **When** a purchase of 10.00 on a weekday is awarded, **Then** the award is 10 points and the tier is Silver
3. **Given** a customer with 0 points, **When** a purchase of 20.00 on Saturday 2026-10-24 is awarded, **Then** the award is 40 points
4. **Given** a customer whose id is empty, **When** a purchase is awarded, **Then** the request is rejected
5. **Given** a purchase total of -1.00, **When** it is awarded, **Then** the request is rejected
6. **Given** 5000 points, **When** the tier is looked up, **Then** it is Gold
7. **Given** a cancelled request, **When** a purchase is awarded, **Then** the award is cancelled

## Requirements *(mandatory)*

- **FR-001**: An award MUST give one point per whole currency unit of the purchase total (rounded down)
- **FR-002**: A purchase made on a Saturday or Sunday (UTC date of the current time) MUST earn double points
- **FR-003**: The tier MUST be Bronze below 1000 points, Silver from 1000 and Gold from 5000, counting the points held after the award
- **FR-004**: An award for an empty customer id MUST be rejected with an ArgumentException
- **FR-005**: A negative purchase total MUST be rejected with an ArgumentOutOfRangeException
- **FR-006**: Awarding MUST be asynchronous and honour cancellation (an OperationCanceledException)
