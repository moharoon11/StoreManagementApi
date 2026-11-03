USE SubscriptionDatabase;

-- Move the hardcoded plan terms into one JSON array on the existing plans table.
-- Existing amounts are copied first so current plans keep their prices during migration.
ALTER TABLE plans ADD COLUMN pricing_json JSON NULL AFTER limits_json;

UPDATE plans
SET pricing_json = JSON_ARRAY(
    JSON_OBJECT(
        'id', 1,
        'code', 'monthly',
        'displayName', 'Monthly',
        'durationDays', 30,
        'amountMinor', monthly_amount_minor,
        'currency', currency,
        'active', TRUE,
        'sortOrder', 1
    ),
    JSON_OBJECT(
        'id', 2,
        'code', 'three_month',
        'displayName', '3 Months',
        'durationDays', 90,
        'amountMinor', three_month_amount_minor,
        'currency', currency,
        'active', three_month_amount_minor > 0,
        'sortOrder', 2
    ),
    JSON_OBJECT(
        'id', 3,
        'code', 'six_month',
        'displayName', '6 Months',
        'durationDays', 180,
        'amountMinor', six_month_amount_minor,
        'currency', currency,
        'active', six_month_amount_minor > 0,
        'sortOrder', 3
    ),
    JSON_OBJECT(
        'id', 4,
        'code', 'yearly',
        'displayName', '1 Year',
        'durationDays', 365,
        'amountMinor', yearly_amount_minor,
        'currency', currency,
        'active', yearly_amount_minor > 0,
        'sortOrder', 4
    )
)
WHERE pricing_json IS NULL;

ALTER TABLE plans MODIFY COLUMN pricing_json JSON NOT NULL;

-- Seed the StoreManagement plan with the options used by the Flutter app.
-- The free trial is controlled by trial_days below and is intentionally not a
-- paid pricing option. Add or remove future paid options in this JSON array.
UPDATE plans
SET trial_days = 7,
    pricing_json = JSON_ARRAY(
        JSON_OBJECT(
            'id', 1,
            'code', 'three_month',
            'displayName', '3 Months',
            'durationSeconds', 7776000,
            'durationDays', 90,
            'amountMinor', 149700,
            'currency', currency,
            'active', TRUE,
            'sortOrder', 1
        ),
        JSON_OBJECT(
            'id', 2,
            'code', 'six_month',
            'displayName', '6 Months',
            'durationSeconds', 15552000,
            'durationDays', 180,
            'amountMinor', 269700,
            'currency', currency,
            'active', TRUE,
            'sortOrder', 2
        ),
        JSON_OBJECT(
            'id', 3,
            'code', 'yearly',
            'displayName', '1 Year',
            'durationSeconds', 31536000,
            'durationDays', 365,
            'amountMinor', 479400,
            'currency', currency,
            'active', TRUE,
            'sortOrder', 3
        ),
        JSON_OBJECT(
            'id', 4,
            'code', 'five_minutes',
            'displayName', '5 Minutes',
            'durationSeconds', 300,
            'durationDays', 1,
            'amountMinor', 100,
            'currency', currency,
            'active', TRUE,
            'sortOrder', 4
        ),
        JSON_OBJECT(
            'id', 5,
            'code', 'fifteen_minutes',
            'displayName', '15 Minutes',
            'durationSeconds', 900,
            'durationDays', 1,
            'amountMinor', 300,
            'currency', currency,
            'active', TRUE,
            'sortOrder', 5
        )
    )
WHERE id = '2b8503d1-a594-47ab-af3d-380a1af28402'
  AND application_id = '7f5a35f8-3ca3-4cc2-9be9-6d7513a24401';

-- Store the selected duration with the subscription so future plan edits do not
-- change the end date of an existing subscription.
ALTER TABLE subscriptions ADD COLUMN duration_days INT NOT NULL DEFAULT 30 AFTER billing_period;

UPDATE subscriptions
SET duration_days = CASE billing_period
    WHEN 'three_month' THEN 90
    WHEN 'six_month' THEN 180
    WHEN 'one_year' THEN 365
    WHEN 'yearly' THEN 365
    ELSE 30
END;

-- The JSON migration is now complete, so remove the old fixed-term columns.
-- These columns must not remain because future plans are stored only in pricing_json.
-- Direct ALTER statements avoid requiring Allow User Variables=true.
ALTER TABLE plans DROP COLUMN monthly_amount_minor;
ALTER TABLE plans DROP COLUMN yearly_amount_minor;
ALTER TABLE plans DROP COLUMN three_month_amount_minor;
ALTER TABLE plans DROP COLUMN six_month_amount_minor;
