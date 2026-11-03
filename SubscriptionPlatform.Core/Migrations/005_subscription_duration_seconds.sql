USE SubscriptionDatabase;

-- Allow short development plans such as 5, 10, and 15 minutes.
ALTER TABLE subscriptions ADD COLUMN duration_seconds INT NOT NULL DEFAULT 2592000 AFTER duration_days;

UPDATE subscriptions
SET duration_seconds = duration_days * 86400;
