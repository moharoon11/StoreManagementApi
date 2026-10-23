CREATE DATABASE IF NOT EXISTS SubscriptionDatabase CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
USE SubscriptionDatabase;

CREATE TABLE IF NOT EXISTS registered_applications (
 id CHAR(36) PRIMARY KEY, name VARCHAR(120) NOT NULL, key_hash CHAR(64) NOT NULL UNIQUE,
 active BOOLEAN NOT NULL DEFAULT TRUE, created_utc DATETIME(6) NOT NULL
);
CREATE TABLE IF NOT EXISTS plans (
 id CHAR(36) PRIMARY KEY, application_id CHAR(36) NOT NULL, name VARCHAR(120) NOT NULL,
 currency CHAR(3) NOT NULL, monthly_amount_minor BIGINT NOT NULL, yearly_amount_minor BIGINT NOT NULL,
 three_month_amount_minor BIGINT NOT NULL DEFAULT 0, six_month_amount_minor BIGINT NOT NULL DEFAULT 0,
 trial_days INT NOT NULL DEFAULT 7, features_json JSON NOT NULL, limits_json JSON NOT NULL, active BOOLEAN NOT NULL DEFAULT TRUE,
 UNIQUE KEY uq_plan_name(application_id,name), CONSTRAINT fk_plan_app FOREIGN KEY(application_id) REFERENCES registered_applications(id)
);
CREATE TABLE IF NOT EXISTS customers (
 id CHAR(36) PRIMARY KEY, application_id CHAR(36) NOT NULL, external_id VARCHAR(200) NOT NULL,
 email VARCHAR(320) NULL, created_utc DATETIME(6) NOT NULL, UNIQUE KEY uq_customer_external(application_id,external_id),
 CONSTRAINT fk_customer_app FOREIGN KEY(application_id) REFERENCES registered_applications(id)
);
CREATE TABLE IF NOT EXISTS subscriptions (
 id CHAR(36) PRIMARY KEY, application_id CHAR(36) NOT NULL, customer_id CHAR(36) NOT NULL, plan_id CHAR(36) NOT NULL,
 status VARCHAR(30) NOT NULL, billing_period VARCHAR(12) NOT NULL, starts_utc DATETIME(6) NOT NULL, ends_utc DATETIME(6) NOT NULL,
 grace_ends_utc DATETIME(6) NULL, cancel_at_period_end BOOLEAN NOT NULL DEFAULT FALSE, version INT UNSIGNED NOT NULL DEFAULT 0,
 created_utc DATETIME(6) NOT NULL, INDEX ix_subscription_owner_status(customer_id,status),
 CONSTRAINT fk_sub_app FOREIGN KEY(application_id) REFERENCES registered_applications(id),
 CONSTRAINT fk_sub_customer FOREIGN KEY(customer_id) REFERENCES customers(id), CONSTRAINT fk_sub_plan FOREIGN KEY(plan_id) REFERENCES plans(id)
);
CREATE TABLE IF NOT EXISTS payments (
 id CHAR(36) PRIMARY KEY, subscription_id CHAR(36) NOT NULL, idempotency_key VARCHAR(80) NOT NULL UNIQUE,
 razorpay_order_id VARCHAR(80) NULL, razorpay_payment_id VARCHAR(80) NULL UNIQUE, amount_minor BIGINT NOT NULL,
 currency CHAR(3) NOT NULL, status VARCHAR(24) NOT NULL, created_utc DATETIME(6) NOT NULL, paid_utc DATETIME(6) NULL,
 CONSTRAINT fk_payment_subscription FOREIGN KEY(subscription_id) REFERENCES subscriptions(id)
);
CREATE TABLE IF NOT EXISTS webhook_events (
 id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, event_id VARCHAR(80) NOT NULL UNIQUE,
 event_type VARCHAR(80) NOT NULL, received_utc DATETIME(6) NOT NULL
);
CREATE TABLE IF NOT EXISTS subscription_audits (
 id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, subscription_id CHAR(36) NOT NULL,
 from_status VARCHAR(30) NOT NULL, to_status VARCHAR(30) NOT NULL, reason VARCHAR(200) NOT NULL,
 at_utc DATETIME(6) NOT NULL, INDEX ix_audit_subscription_time(subscription_id,at_utc),
 CONSTRAINT fk_audit_subscription FOREIGN KEY(subscription_id) REFERENCES subscriptions(id)
);
