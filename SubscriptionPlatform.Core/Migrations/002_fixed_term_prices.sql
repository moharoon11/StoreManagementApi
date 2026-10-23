USE SubscriptionDatabase;
ALTER TABLE plans
  ADD COLUMN three_month_amount_minor BIGINT NOT NULL DEFAULT 0 AFTER yearly_amount_minor,
  ADD COLUMN six_month_amount_minor BIGINT NOT NULL DEFAULT 0 AFTER three_month_amount_minor;
ALTER TABLE customers MODIFY email VARCHAR(320) NULL;
