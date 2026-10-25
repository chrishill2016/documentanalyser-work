# Project Falcon: Weekly Update (Week 42)

## Summary
Project Falcon is migrating the customer portal from the legacy on-prem stack to Azure App Service.
Overall status is **Amber**: build and test are on track but data migration is behind schedule.

## Progress this week
- The new authentication flow (Entra ID) passed security review on Tuesday.
- 14 of 20 portal pages have been rebuilt; the remaining 6 are the billing pages.
- Load testing showed p95 response time of 420 ms against a target of 500 ms.

## Risks and issues
1. **Data migration delay**: the export from the legacy SQL Server is taking 3x longer than estimated.
   Priya Shah is investigating bulk-copy options. If not resolved by 31 October the go-live date of
   14 November is at risk.
2. **Billing vendor API**: the vendor has not yet confirmed the v3 API rate limits. Tom Becker to chase.
3. **Budget**: spend is at 82% with roughly 70% of the work complete.

## Decisions needed
- Approve an extra £12,000 for a temporary contractor to help with data migration (owner: Sarah Lee, steering group).
- Decide whether billing pages can go live one sprint after the rest of the portal.

## Next steps
- Priya: present bulk-copy findings at Thursday's stand-up.
- Tom: get written confirmation of rate limits from the billing vendor by Friday.
- Sarah: take the contractor request to steering group on 28 October.
