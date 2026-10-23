USE SubscriptionDatabase;

-- This upgrade is for databases created from the older initial schema.
-- Check each column before adding it so rerunning the script, or running it
-- after the current initial schema, does not fail with a duplicate-column error.
SET @has_three_month_amount = (
  SELECT COUNT(*)
  FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME = 'plans'
    AND COLUMN_NAME = 'three_month_amount_minor'
);
SET @sql = IF(
  @has_three_month_amount = 0,
  'ALTER TABLE plans ADD COLUMN three_month_amount_minor BIGINT NOT NULL DEFAULT 0 AFTER yearly_amount_minor',
  'SELECT ''three_month_amount_minor already exists'' AS migration_status'
);
PREPARE migration_stmt FROM @sql;
EXECUTE migration_stmt;
DEALLOCATE PREPARE migration_stmt;

SET @has_six_month_amount = (
  SELECT COUNT(*)
  FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME = 'plans'
    AND COLUMN_NAME = 'six_month_amount_minor'
);
SET @sql = IF(
  @has_six_month_amount = 0,
  'ALTER TABLE plans ADD COLUMN six_month_amount_minor BIGINT NOT NULL DEFAULT 0 AFTER three_month_amount_minor',
  'SELECT ''six_month_amount_minor already exists'' AS migration_status'
);
PREPARE migration_stmt FROM @sql;
EXECUTE migration_stmt;
DEALLOCATE PREPARE migration_stmt;

-- Safe to repeat when the email column already has this definition.
ALTER TABLE customers MODIFY COLUMN email VARCHAR(320) NULL;
