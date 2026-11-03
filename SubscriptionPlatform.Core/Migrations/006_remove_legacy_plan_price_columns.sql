USE SubscriptionDatabase;

-- Run this migration if 004_dynamic_plan_pricing.sql stopped before the cleanup.
-- These direct statements do not require Allow User Variables=true.
ALTER TABLE plans DROP COLUMN monthly_amount_minor;
ALTER TABLE plans DROP COLUMN yearly_amount_minor;
ALTER TABLE plans DROP COLUMN three_month_amount_minor;
ALTER TABLE plans DROP COLUMN six_month_amount_minor;
