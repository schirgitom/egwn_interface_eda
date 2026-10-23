# EGWN EDA Interface

EDA credentials can be configured as a `Communities` array under `Eda`. Each entry needs a display `Name`, `CommunityId`, `Username`, and `Password`; the EDA endpoint URLs and timeout remain shared:

```json
{
  "Eda": {
    "Communities": [
      {
        "Name": "Energiegemeinschaft Nord",
        "CommunityId": "<eda-community-id>",
        "Username": "<eda-username>",
        "Password": "<eda-password>"
      },
      {
        "Name": "Energiegemeinschaft Süd",
        "CommunityId": "<eda-community-id>",
        "Username": "<eda-username>",
        "Password": "<eda-password>"
      }
    ]
  }
}
```

Keep real credentials in the deployment secret store or Consul, not in source control. Existing single-community `Eda:CommunityId`, `Eda:Username`, and `Eda:Password` settings remain supported.

RabbitMQ publications include both `communityId` and `communityName`, allowing persistence consumers to distinguish and label readings from different communities. KPI triggers without a `communityId` process every configured community; callers can pass a `communityId` in the request to select one.

Historical backfills cover the last two years. To backfill one configured community, pass its ID as a query parameter:

```text
POST /api/readings/backfill/meter?communityId=<eda-community-id>
POST /api/readings/backfill/kpi?communityId=<eda-community-id>
```

Omitting `communityId` keeps the existing behavior and processes all applicable communities.
