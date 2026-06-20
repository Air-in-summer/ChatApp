CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260321113946_InitialCreate') THEN
    CREATE TABLE "Users" (
        "Id" uuid NOT NULL,
        "Username" character varying(50) NOT NULL,
        "DisplayName" character varying(100) NOT NULL,
        "Email" character varying(255) NOT NULL,
        "PasswordHash" text NOT NULL,
        "IsActive" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_Users" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260321113946_InitialCreate') THEN
    CREATE TABLE "RefreshTokens" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "Token" text NOT NULL,
        "ExpiresAt" timestamp with time zone NOT NULL,
        "IsRevoked" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_RefreshTokens" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_RefreshTokens_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260321113946_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_RefreshTokens_Token" ON "RefreshTokens" ("Token");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260321113946_InitialCreate') THEN
    CREATE INDEX "IX_RefreshTokens_UserId" ON "RefreshTokens" ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260321113946_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_Users_Email" ON "Users" ("Email");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260321113946_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_Users_Username" ON "Users" ("Username");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260321113946_InitialCreate') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260321113946_InitialCreate', '8.0.28');
    END IF;
END $EF$;
COMMIT;
START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260328104006_AddUserUpdatedAt') THEN
    ALTER TABLE "Users" ADD "UpdatedAt" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260328104006_AddUserUpdatedAt') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260328104006_AddUserUpdatedAt', '8.0.28');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260328104851_AddRoomEntities') THEN
    CREATE TABLE "Rooms" (
        "Id" uuid NOT NULL,
        "GroupId" uuid,
        "Type" character varying(20) NOT NULL,
        "Name" character varying(100),
        "IsPrivate" boolean NOT NULL,
        "MaxMembers" integer,
        "CreatedBy" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        CONSTRAINT "PK_Rooms" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_Rooms_Users_CreatedBy" FOREIGN KEY ("CreatedBy") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260328104851_AddRoomEntities') THEN
    CREATE TABLE "RoomMembers" (
        "RoomId" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "Role" character varying(20) NOT NULL,
        "JoinedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_RoomMembers" PRIMARY KEY ("RoomId", "UserId"),
        CONSTRAINT "FK_RoomMembers_Rooms_RoomId" FOREIGN KEY ("RoomId") REFERENCES "Rooms" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_RoomMembers_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260328104851_AddRoomEntities') THEN
    CREATE INDEX "IX_RoomMembers_UserId" ON "RoomMembers" ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260328104851_AddRoomEntities') THEN
    CREATE INDEX "IX_Rooms_CreatedBy" ON "Rooms" ("CreatedBy");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260328104851_AddRoomEntities') THEN
    CREATE INDEX "IX_Rooms_GroupId" ON "Rooms" ("GroupId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260328104851_AddRoomEntities') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260328104851_AddRoomEntities', '8.0.28');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260328170832_OptimizeRoomMemberIndex') THEN
    DROP INDEX "IX_RoomMembers_UserId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260328170832_OptimizeRoomMemberIndex') THEN
    CREATE INDEX "IX_RoomMembers_UserId_RoomId" ON "RoomMembers" ("UserId", "RoomId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260328170832_OptimizeRoomMemberIndex') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260328170832_OptimizeRoomMemberIndex', '8.0.28');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260406043135_AddUserTrigramIndexes') THEN
    CREATE EXTENSION IF NOT EXISTS pg_trgm;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260406043135_AddUserTrigramIndexes') THEN
    CREATE INDEX "IX_Users_DisplayName_Trgm" ON "Users" USING gin ("DisplayName" gin_trgm_ops);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260406043135_AddUserTrigramIndexes') THEN
    CREATE INDEX "IX_Users_Username_Trgm" ON "Users" USING gin ("Username" gin_trgm_ops);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260406043135_AddUserTrigramIndexes') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260406043135_AddUserTrigramIndexes', '8.0.28');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260425025301_AddReadReceipts') THEN
    CREATE TABLE "ReadReceipts" (
        "UserId" uuid NOT NULL,
        "RoomId" uuid NOT NULL,
        "LastReadMessageId" text NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_ReadReceipts" PRIMARY KEY ("UserId", "RoomId"),
        CONSTRAINT "FK_ReadReceipts_Rooms_RoomId" FOREIGN KEY ("RoomId") REFERENCES "Rooms" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_ReadReceipts_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260425025301_AddReadReceipts') THEN
    CREATE INDEX "IX_ReadReceipts_RoomId" ON "ReadReceipts" ("RoomId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260425025301_AddReadReceipts') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260425025301_AddReadReceipts', '8.0.28');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260501092042_AddGroupModule') THEN
    CREATE TABLE "Groups" (
        "Id" uuid NOT NULL,
        "Name" character varying(100) NOT NULL,
        "Description" character varying(255),
        "IconUrl" text,
        "InviteCode" character varying(20) NOT NULL,
        "OwnerId" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        "DeletedAt" timestamp with time zone,
        CONSTRAINT "PK_Groups" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_Groups_Users_OwnerId" FOREIGN KEY ("OwnerId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260501092042_AddGroupModule') THEN
    CREATE TABLE "GroupMembers" (
        "GroupId" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "JoinedAt" timestamp with time zone NOT NULL,
        "Role" character varying(20) NOT NULL,
        CONSTRAINT "PK_GroupMembers" PRIMARY KEY ("GroupId", "UserId"),
        CONSTRAINT "FK_GroupMembers_Groups_GroupId" FOREIGN KEY ("GroupId") REFERENCES "Groups" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_GroupMembers_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260501092042_AddGroupModule') THEN
    CREATE INDEX "IX_GroupMembers_UserId_GroupId" ON "GroupMembers" ("UserId", "GroupId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260501092042_AddGroupModule') THEN
    CREATE UNIQUE INDEX "IX_Groups_InviteCode" ON "Groups" ("InviteCode");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260501092042_AddGroupModule') THEN
    CREATE INDEX "IX_Groups_OwnerId" ON "Groups" ("OwnerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260501092042_AddGroupModule') THEN
    ALTER TABLE "Rooms" ADD CONSTRAINT "FK_Rooms_Groups_GroupId" FOREIGN KEY ("GroupId") REFERENCES "Groups" ("Id") ON DELETE CASCADE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260501092042_AddGroupModule') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260501092042_AddGroupModule', '8.0.28');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260519163800_HashRefreshTokenStorage') THEN
    ALTER TABLE "RefreshTokens" RENAME COLUMN "Token" TO "TokenHash";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260519163800_HashRefreshTokenStorage') THEN
    ALTER INDEX "IX_RefreshTokens_Token" RENAME TO "IX_RefreshTokens_TokenHash";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260519163800_HashRefreshTokenStorage') THEN
    CREATE EXTENSION IF NOT EXISTS pgcrypto;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260519163800_HashRefreshTokenStorage') THEN
    UPDATE "RefreshTokens" SET "TokenHash" = encode(digest("TokenHash", 'sha256'), 'base64');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260519163800_HashRefreshTokenStorage') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260519163800_HashRefreshTokenStorage', '8.0.28');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260519170613_AddUserExternalLoginProfileSchema') THEN
    ALTER TABLE "Users" ALTER COLUMN "PasswordHash" DROP NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260519170613_AddUserExternalLoginProfileSchema') THEN
    ALTER TABLE "Users" ADD "AvatarUrl" character varying(2048);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260519170613_AddUserExternalLoginProfileSchema') THEN
    CREATE TABLE "ExternalLogins" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "Provider" character varying(50) NOT NULL,
        "ProviderUserId" character varying(255) NOT NULL,
        "ProviderEmail" character varying(255) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone,
        CONSTRAINT "PK_ExternalLogins" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_ExternalLogins_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260519170613_AddUserExternalLoginProfileSchema') THEN
    CREATE UNIQUE INDEX "IX_ExternalLogins_Provider_ProviderUserId" ON "ExternalLogins" ("Provider", "ProviderUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260519170613_AddUserExternalLoginProfileSchema') THEN
    CREATE INDEX "IX_ExternalLogins_UserId" ON "ExternalLogins" ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260519170613_AddUserExternalLoginProfileSchema') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260519170613_AddUserExternalLoginProfileSchema', '8.0.28');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260522143256_AddSocialFoundation') THEN
    CREATE TABLE "FriendRequests" (
        "Id" uuid NOT NULL,
        "RequesterId" uuid NOT NULL,
        "ReceiverId" uuid NOT NULL,
        "Status" character varying(20) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "RespondedAt" timestamp with time zone,
        "CanceledAt" timestamp with time zone,
        CONSTRAINT "PK_FriendRequests" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_FriendRequests_NotSelf" CHECK ("RequesterId" <> "ReceiverId"),
        CONSTRAINT "FK_FriendRequests_Users_ReceiverId" FOREIGN KEY ("ReceiverId") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_FriendRequests_Users_RequesterId" FOREIGN KEY ("RequesterId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260522143256_AddSocialFoundation') THEN
    CREATE TABLE "Friendships" (
        "UserAId" uuid NOT NULL,
        "UserBId" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_Friendships" PRIMARY KEY ("UserAId", "UserBId"),
        CONSTRAINT "CK_Friendships_NormalizedPair" CHECK ("UserAId" < "UserBId"),
        CONSTRAINT "FK_Friendships_Users_UserAId" FOREIGN KEY ("UserAId") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_Friendships_Users_UserBId" FOREIGN KEY ("UserBId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260522143256_AddSocialFoundation') THEN
    CREATE TABLE "UserBlocks" (
        "BlockerId" uuid NOT NULL,
        "BlockedId" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_UserBlocks" PRIMARY KEY ("BlockerId", "BlockedId"),
        CONSTRAINT "CK_UserBlocks_NotSelf" CHECK ("BlockerId" <> "BlockedId"),
        CONSTRAINT "FK_UserBlocks_Users_BlockedId" FOREIGN KEY ("BlockedId") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_UserBlocks_Users_BlockerId" FOREIGN KEY ("BlockerId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260522143256_AddSocialFoundation') THEN
    CREATE TABLE "UserPresenceStates" (
        "UserId" uuid NOT NULL,
        "LastSeenAt" timestamp with time zone,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_UserPresenceStates" PRIMARY KEY ("UserId"),
        CONSTRAINT "FK_UserPresenceStates_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260522143256_AddSocialFoundation') THEN
    CREATE INDEX "IX_FriendRequests_ReceiverId_Status_CreatedAt" ON "FriendRequests" ("ReceiverId", "Status", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260522143256_AddSocialFoundation') THEN
    CREATE INDEX "IX_FriendRequests_RequesterId_ReceiverId_Status" ON "FriendRequests" ("RequesterId", "ReceiverId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260522143256_AddSocialFoundation') THEN
    CREATE INDEX "IX_FriendRequests_RequesterId_Status_CreatedAt" ON "FriendRequests" ("RequesterId", "Status", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260522143256_AddSocialFoundation') THEN
    CREATE INDEX "IX_Friendships_UserBId_UserAId" ON "Friendships" ("UserBId", "UserAId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260522143256_AddSocialFoundation') THEN
    CREATE INDEX "IX_UserBlocks_BlockedId_BlockerId" ON "UserBlocks" ("BlockedId", "BlockerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260522143256_AddSocialFoundation') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260522143256_AddSocialFoundation', '8.0.28');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260525123653_AddMediaAssets') THEN
    CREATE TABLE "MediaAssets" (
        "Id" uuid NOT NULL,
        "OwnerUserId" uuid NOT NULL,
        "Scope" character varying(30) NOT NULL,
        "Kind" character varying(20) NOT NULL,
        "AccessLevel" character varying(30) NOT NULL,
        "Status" character varying(20) NOT NULL,
        "RoomId" uuid,
        "MessageId" character varying(64),
        "BucketName" character varying(120) NOT NULL,
        "StorageKey" character varying(1024) NOT NULL,
        "OriginalFileName" character varying(255) NOT NULL,
        "ContentType" character varying(120) NOT NULL,
        "SizeBytes" bigint NOT NULL,
        "ChecksumSha256" character varying(64),
        "Width" integer,
        "Height" integer,
        "DurationMs" integer,
        "PublicUrl" character varying(2048),
        "ThumbnailMediaId" uuid,
        "CreatedAt" timestamp with time zone NOT NULL,
        "AttachedAt" timestamp with time zone,
        "DeletedAt" timestamp with time zone,
        CONSTRAINT "PK_MediaAssets" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_MediaAssets_SizeBytes_NonNegative" CHECK ("SizeBytes" >= 0),
        CONSTRAINT "FK_MediaAssets_Users_OwnerUserId" FOREIGN KEY ("OwnerUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260525123653_AddMediaAssets') THEN
    CREATE UNIQUE INDEX "IX_MediaAssets_BucketName_StorageKey" ON "MediaAssets" ("BucketName", "StorageKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260525123653_AddMediaAssets') THEN
    CREATE INDEX "IX_MediaAssets_OwnerUserId_CreatedAt" ON "MediaAssets" ("OwnerUserId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260525123653_AddMediaAssets') THEN
    CREATE INDEX "IX_MediaAssets_RoomId_CreatedAt" ON "MediaAssets" ("RoomId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260525123653_AddMediaAssets') THEN
    CREATE INDEX "IX_MediaAssets_Scope_OwnerUserId" ON "MediaAssets" ("Scope", "OwnerUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260525123653_AddMediaAssets') THEN
    CREATE INDEX "IX_MediaAssets_Status_CreatedAt" ON "MediaAssets" ("Status", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260525123653_AddMediaAssets') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260525123653_AddMediaAssets', '8.0.28');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260525124227_RemoveUnusedMediaAssetOptionalFields') THEN
    ALTER TABLE "MediaAssets" DROP COLUMN "ChecksumSha256";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260525124227_RemoveUnusedMediaAssetOptionalFields') THEN
    ALTER TABLE "MediaAssets" DROP COLUMN "DurationMs";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260525124227_RemoveUnusedMediaAssetOptionalFields') THEN
    ALTER TABLE "MediaAssets" DROP COLUMN "Height";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260525124227_RemoveUnusedMediaAssetOptionalFields') THEN
    ALTER TABLE "MediaAssets" DROP COLUMN "ThumbnailMediaId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260525124227_RemoveUnusedMediaAssetOptionalFields') THEN
    ALTER TABLE "MediaAssets" DROP COLUMN "Width";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260525124227_RemoveUnusedMediaAssetOptionalFields') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260525124227_RemoveUnusedMediaAssetOptionalFields', '8.0.28');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260606062742_AddMediaAssetReservations') THEN
    ALTER TABLE "MediaAssets" ADD "ReservedAt" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260606062742_AddMediaAssetReservations') THEN
    ALTER TABLE "MediaAssets" ADD "ReservedByMessageId" character varying(64);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260606062742_AddMediaAssetReservations') THEN
    CREATE INDEX "IX_MediaAssets_Status_ReservedAt" ON "MediaAssets" ("Status", "ReservedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260606062742_AddMediaAssetReservations') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260606062742_AddMediaAssetReservations', '8.0.28');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260607133000_AddRoomSoftDelete') THEN
    ALTER TABLE "Rooms" ADD "DeletedAt" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260607133000_AddRoomSoftDelete') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260607133000_AddRoomSoftDelete', '8.0.28');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260607174247_AddAuthSessions') THEN
    CREATE TABLE "AuthSessions" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "TokenHash" character varying(128) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "ExpiresAt" timestamp with time zone NOT NULL,
        "LastSeenAt" timestamp with time zone NOT NULL,
        "RevokedAt" timestamp with time zone,
        "RevokedReason" character varying(200),
        "CreatedByIp" character varying(64),
        "LastSeenIp" character varying(64),
        "UserAgent" character varying(512),
        CONSTRAINT "PK_AuthSessions" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AuthSessions_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260607174247_AddAuthSessions') THEN
    CREATE INDEX "IX_AuthSessions_ExpiresAt_RevokedAt" ON "AuthSessions" ("ExpiresAt", "RevokedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260607174247_AddAuthSessions') THEN
    CREATE UNIQUE INDEX "IX_AuthSessions_TokenHash" ON "AuthSessions" ("TokenHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260607174247_AddAuthSessions') THEN
    CREATE INDEX "IX_AuthSessions_UserId_ExpiresAt" ON "AuthSessions" ("UserId", "ExpiresAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260607174247_AddAuthSessions') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260607174247_AddAuthSessions', '8.0.28');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260613093000_DropRefreshTokens') THEN
    DROP TABLE "RefreshTokens";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260613093000_DropRefreshTokens') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260613093000_DropRefreshTokens', '8.0.28');
    END IF;
END $EF$;
COMMIT;
