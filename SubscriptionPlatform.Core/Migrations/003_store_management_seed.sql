-- One-time StoreManagement application and sample plan seed.
-- Run this against the same database where 001_initial.sql and 002_fixed_term_prices.sql ran.
-- Prices are placeholders for setup/testing only. Replace them with your real prices
-- before accepting live payments. INR amounts are in paise (149700 = Rs 1,497.00).

INSERT INTO registered_applications (id, name, key_hash, active, created_utc)
VALUES (
    '7f5a35f8-3ca3-4cc2-9be9-6d7513a24401',
    'StoreManagement',
    'f08ee8874abcdd56f1a38da7a02ee694e457d9a9de82eee28a31ed7620c49ca2',
    TRUE,
    UTC_TIMESTAMP(6)
)
ON DUPLICATE KEY UPDATE id = VALUES(id);

INSERT INTO plans (
    id, application_id, name, currency,
    monthly_amount_minor, three_month_amount_minor, six_month_amount_minor,
    yearly_amount_minor, trial_days, features_json, limits_json, active
)
VALUES (
    '2b8503d1-a594-47ab-af3d-380a1af28402',
    '7f5a35f8-3ca3-4cc2-9be9-6d7513a24401',
    'StoreManagement Sample Plan',
    'INR',
    0,
    149700,
    269700,
    479400,
    7,
    JSON_OBJECT(),
    JSON_OBJECT(),
    TRUE
)
ON DUPLICATE KEY UPDATE id = VALUES(id);
