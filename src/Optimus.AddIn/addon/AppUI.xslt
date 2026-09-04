<?xml version="1.0"?>
<!--
  Optimus Add-on - AppUI.xslt (DOCKER)

  Defines, by appending to the live uiConfig:
    1) a toggle checkButton (guid b1665682…) that shows/hides the Optimus docker;
    2) a wpfhost item (guid 6fec0299…) hosting the .NET docker UserControl
       Optimus.AddIn.Ui.OptimusDocker out of Addons\Optimus\Optimus.AddIn.dll;
    3) the anchored docker itself (guid bb0ac689…) containing that host.

  The button is PLACED into the Tools menu + Standard toolbar by UserUI.xslt. The
  check="*Docker('<guid>')" syntax is the real CorelDRAW docker-toggle; type="wpfhost" +
  hostedType is the .NET-addon hosting contract. uiConfig is in NO namespace, so match
  patterns are unprefixed. UTF-8, no BOM. Relaunch CorelDRAW holding F8 after changing this.

  icon="guid://<self>" pulls the Optimus mark from Optimus.Resources.dll via addon/config.xml
  (resEntry id=this GUID -> icon 101), so the toolbar button shows the orange mark, not a
  blank slot.
-->
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:frmwrk="Corel Framework Data">
  <xsl:output method="xml" encoding="UTF-8" indent="yes"/>

  <!-- applicationInfo MUST be the topmost frmwrk element; moves our items into user config. -->
  <frmwrk:uiconfig>
    <frmwrk:applicationInfo userConfiguration="true" />
  </frmwrk:uiconfig>

  <!-- Identity transform: copy the existing UI unchanged. -->
  <xsl:template match="node()|@*">
    <xsl:copy>
      <xsl:apply-templates select="node()|@*"/>
    </xsl:copy>
  </xsl:template>

  <!-- 1) the toggle button + 2) the WPF-hosted docker content control. -->
  <xsl:template match="uiConfig/items">
    <xsl:copy>
      <xsl:apply-templates select="node()|@*"/>

      <itemData guid="b1665682-2d27-445a-96d0-45af18a2504c"
                type="checkButton"
                check="*Docker('bb0ac689-b8e1-4bcc-bf0f-769bd5e67a50')"
                icon="guid://b1665682-2d27-445a-96d0-45af18a2504c"
                userCaption="Optimus"
                userToolTip="Optimus &#8212; Otimizador de arquivos CorelDRAW"/>

      <itemData guid="6fec0299-0eac-4147-912e-ed46684bac04"
                type="wpfhost"
                hostedType="Addons\Optimus\Optimus.AddIn.dll,Optimus.AddIn.Ui.OptimusDocker"
                enable="true"/>
    </xsl:copy>
  </xsl:template>

  <!-- 3) the anchored docker, hosting the control (fill the whole docker). -->
  <xsl:template match="uiConfig/dockers">
    <xsl:copy>
      <xsl:apply-templates select="node()|@*"/>
      <dockerData guid="bb0ac689-b8e1-4bcc-bf0f-769bd5e67a50"
                  userCaption="Optimus"
                  wantReturn="true">
        <container focusStyle="noThrow">
          <item dock="fill" guidRef="6fec0299-0eac-4147-912e-ed46684bac04"/>
        </container>
      </dockerData>
    </xsl:copy>
  </xsl:template>

</xsl:stylesheet>
