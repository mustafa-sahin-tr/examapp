---
name: feedback-chart-style
description: User rejected ngx-charts line charts on the admin dashboard; wants GitHub-style calendar heatmaps identical to the student dashboard (52 weeks x 7 days)
metadata:
  type: feedback
---

For time-series visualisations, reuse the student dashboard heatmap pattern (`ngx-charts-heat-map`, outer array = weeks on x, 7 reversed day cells on y, Linear colour scheme) instead of line charts.

**Why:** On issue #88 (admin dashboard trends) the user reviewed the ngx-charts line-chart version live and explicitly disliked the look ("öğrenci dashboard'undaki heatmap'e benzeyen yapı daha mantıklı"). He then widened the window from 30 days to "Son 1 Yıl" so it matches the student dashboard exactly (52 weeks, each column = one week).

**How to apply:** When adding any activity/trend chart in `ui/`, start from `dashboard.component.ts` `transformActivityToHeatmap` / `activityHeatmapView` / `activityXAxisTickFormatting` and copy the orientation and sizing; do not introduce a new chart type without asking. Colours via CSS tokens resolved at runtime (`readCssToken`), light-to-full stops built with rgba alpha because d3 cannot parse `color-mix()`.
