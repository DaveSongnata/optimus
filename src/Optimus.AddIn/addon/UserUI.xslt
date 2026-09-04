<?xml version="1.0"?>
<!--
  Optimus Add-on - UserUI.xslt
  PLACES the Optimus button (defined in AppUI.xslt as itemData guid b1665682-…) into
  two always-visible containers. Applied once per workspace; after editing it, launch
  CorelDRAW holding F8 to force a workspace reset (back up the Workspace folder first).

  Target GUIDs (Tools menu / Standard toolbar / separator) are the live CorelDRAW 2024
  DrawUI.xml containers, in NO namespace — match patterns are unprefixed:
    Tools menu       : commandBarData guid 6f114d89-1b8c-4877-a4af-a3624ddd95f6  (child <menu>)
    Standard toolbar : commandBarData guid c2b44f69-6dec-444e-a37e-5dbf7ff43dae  (child <toolbar>)
    Separator item   : guidRef 266435b4-6e53-460f-9fa7-f45be187d400
    Optimus item     : guidRef b1665682-2d27-445a-96d0-45af18a2504c  (ours; from AppUI.xslt)

  Both placements are IDEMPOTENT (xsl:if test="not(...)") so an F8 re-apply never duplicates.
-->
<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:frmwrk="Corel Framework Data"
                exclude-result-prefixes="frmwrk">
  <xsl:output method="xml" encoding="UTF-8" indent="yes"/>

  <frmwrk:uiconfig>
    <frmwrk:applicationInfo userConfiguration="true" />
    <frmwrk:compositeNode xPath="/uiConfig/commandBars/commandBarData[@guid='6f114d89-1b8c-4877-a4af-a3624ddd95f6']"/>
    <frmwrk:compositeNode xPath="/uiConfig/commandBars/commandBarData[@guid='c2b44f69-6dec-444e-a37e-5dbf7ff43dae']"/>
    <frmwrk:compositeNode xPath="/uiConfig/frame"/>
  </frmwrk:uiconfig>

  <!-- Identity transform: copy all of the existing user interface unchanged. -->
  <xsl:template match="node()|@*">
    <xsl:copy>
      <xsl:apply-templates select="node()|@*"/>
    </xsl:copy>
  </xsl:template>

  <!-- Tools menu (Ferramentas): append a separator + the Optimus button at the end of its <menu>. -->
  <xsl:template match="uiConfig/commandBars/commandBarData[@guid='6f114d89-1b8c-4877-a4af-a3624ddd95f6']/menu">
    <xsl:copy>
      <xsl:apply-templates select="node()|@*"/>
      <xsl:if test="not(./item[@guidRef='b1665682-2d27-445a-96d0-45af18a2504c'])">
        <item guidRef="266435b4-6e53-460f-9fa7-f45be187d400"/> <!-- separator -->
        <item guidRef="b1665682-2d27-445a-96d0-45af18a2504c"/> <!-- the Optimus button -->
      </xsl:if>
    </xsl:copy>
  </xsl:template>

  <!-- Standard toolbar: append a separator + the Optimus button at the end of its <toolbar>. -->
  <xsl:template match="uiConfig/commandBars/commandBarData[@guid='c2b44f69-6dec-444e-a37e-5dbf7ff43dae']/toolbar">
    <xsl:copy>
      <xsl:apply-templates select="node()|@*"/>
      <xsl:if test="not(./item[@guidRef='b1665682-2d27-445a-96d0-45af18a2504c'])">
        <item guidRef="266435b4-6e53-460f-9fa7-f45be187d400"/> <!-- separator -->
        <item guidRef="b1665682-2d27-445a-96d0-45af18a2504c"/> <!-- the Optimus button -->
      </xsl:if>
    </xsl:copy>
  </xsl:template>

</xsl:stylesheet>
