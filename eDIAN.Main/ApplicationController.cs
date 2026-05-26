using ZwSoft.ZwCAD.Interop;
using ZwSoft.Windows;
using ZwSoft.Windows.ToolBars;
using eDIAN.Core;
using eDIAN.Data;
using eDIAN.Main.Data;
using eDIAN.Main.UI;
using log4net;
using CadApplication = ZwSoft.ZwCAD.ApplicationServices.Application;
using CadCoreApplication = ZwSoft.ZwCAD.ApplicationServices.Core.Application;
using System.Collections.Generic;
using System;
using System.Linq;

namespace eDIAN.Main
{
    public class ApplicationController
    {
        private static ILog logger = PluginLogger.getLogger("ApplicationController", "application.log");

        private static ApplicationController applicationController;

        public ApplicationController()
        {
            this.Initialize();
        }

        public static ApplicationController getInstance()
        {
            if (applicationController == null)
            {
                applicationController = new ApplicationController();
            }

            return applicationController;
        }

        public void Initialize()
        {

        }

        /// <summary>
        /// CAD 화면 메뉴에서 저장/출력/게시 관련 메뉴 항목 비활성화/활성화 처리
        /// </summary>
        /// <param name="commandType">권한 구분(Save, Print, Publish)</param>
        /// <param name="enabled">true : 활성화, false : 비활성화</param>
        public void setApplicationMenuByLabels(ProtectedDocument protectedDocument)
        {
            List<(String, bool)> statusList = new List<(String, bool)>();

            if (protectedDocument == null || protectedDocument.isOwner || protectedDocument.isProtected == false)
            {
                // 클래식 메뉴바 원복
                this.setVisibleClassicMenuBar(DocumentConstants.CLASSIC_MENU_VISIBLE);

                // 리본 메뉴 전체 활성화
                this.setEnableRibbonMenu(null);

                // 리본/퀵 액세스 메뉴 버튼 활성화/비활성화 값 세팅
                statusList.Add(("Common", true));
                statusList.Add(("Save", true));
                statusList.Add(("Publish", true));
                statusList.Add(("Copy", true));
                statusList.Add(("Print", true));
            }
            else
            {
                // 클래식 메뉴바 비활성화
                this.setVisibleClassicMenuBar(false);

                // 리본 메뉴 전체 비활성화
                this.setEnableRibbonMenu(protectedDocument);

                // 리본/퀵 액세스 메뉴 버튼 활성화/비활성화 값 세팅

                statusList.Add(("Common", false));        // 보호문서일 경우 무조건 비활성화 될 메뉴
                statusList.Add(("Save", protectedDocument.isEdit));
                statusList.Add(("Publish", protectedDocument.isEdit));
                statusList.Add(("Copy", protectedDocument.isExtract));
                statusList.Add(("Print", protectedDocument.isPrint));
            }

            foreach ((String commandType, bool enabled) in statusList)
            {
                // 퀵 액세스 툴바 버튼 활성화/비활성화
                this.setEnableQuickAccessToolbarButton(commandType, enabled);

                // 메뉴 콘텐츠 버튼 활성화/비활성화
                this.setEnableMenuContentButton(commandType, enabled);

                // 리본 버튼 활성화/비활성화
                this.setEnableRibbonButton(commandType, enabled);
            }
        }

        // 클래식 메뉴 바 표시/숨김 (MENUBAR)
        private void setVisibleClassicMenuBar(bool visible)
        {
            this.setVisibleClassicMenuBar(visible ? 1 : 0);
        }

        // 클래식 메뉴 바 표시/숨김 (MENUBAR)
        private void setVisibleClassicMenuBar(int systemVariable)
        {
//            logger.Debug("* Set visible of Classic Menu : " + (systemVariable == 1 ? "visible" : "invisible"));

            if (DocumentConstants.CLASSIC_MENU_VISIBLE == 0)
            {
                logger.Debug($" - not changed (invisible)");

                return;
            }

            try
            {
                DocumentConstants.UNLOCK_CLASSIC_MENU = true;

                CadCoreApplication.SetSystemVariable("MENUBAR", systemVariable);

                DocumentConstants.LAST_CLASSIC_MENU_VISIBLE = systemVariable;

                DocumentConstants.UNLOCK_CLASSIC_MENU = false;

//                logger.Debug(systemVariable == 1 ? $" - {systemVariable}(visible)" : $" - {systemVariable}(invisible)");
            }
            catch (Exception ex)
            {
                logger.Debug($" - setVisibleClassicMenuBar error: {ex.Message}");
            }
        }

        // 리본 메뉴 활성화/비활성화를 위한 정적 메서드
        public void setEnableRibbonMenu(ProtectedDocument protectedDocument)
        {
            bool enabled = true;

            if (protectedDocument == null || protectedDocument.isOwner || protectedDocument.isProtected == false)
            {
                enabled = true;
            }
            else if (protectedDocument.isView == true && !protectedDocument.isEdit && !protectedDocument.isExtract && !protectedDocument.isExport && !protectedDocument.isPrint)
            {
                enabled = false;
            }

            try
            {
                // 현재 리본 컨트롤에 접근
                RibbonControl ribbonControl = ComponentManager.Ribbon;

                // 리본 컨트롤의 IsEnabled 속성을 설정하여 활성화/비활성화
                ribbonControl.IsEnabled = enabled;

                // 배경 탭 렌더링도 함께 설정하여 시각적 일관성 유지 (선택 사항)
                ribbonControl.IsBackgroundTabRenderingEnabled = enabled;

                // 리본 레이아웃 갱신

                ribbonControl.UpdateLayout();
            }
            catch (Exception ex)
            {
                logger.Debug($" - setEnableRibbonMenu error: {ex.Message}");
            }
        }

        // 퀵 액세스 툴바 버튼 비활성화/활성화
        private void setEnableQuickAccessToolbarButton(String commandType, bool enabled)
        {
//            logger.Debug($"* Set enable Quick Access Toolbar Buttons : {enabled}");

            try
            {
                QuickAccessToolBarSource quickAccessToolBarSource = ComponentManager.QuickAccessToolBar;

                // 1. 퀵 액세스(좌측 위) 저장 버튼
                if (quickAccessToolBarSource != null)
                {
                    foreach (RibbonItem item in quickAccessToolBarSource.Items)
                    {
                        if (this.lookupButton(item, commandType))
                        {
                            item.IsEnabled = enabled;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 리본 구조는 버전에 따라 달라질 수 있으니 예외를 무시하고 로그만 남김
                logger.Debug($" - setEnableQuickAccessToolbarButton error: {ex.Message}");
            }
        }


        // 메뉴 컨텐트(A 버튼) 버튼 활성화 비활성화 
        private void setEnableMenuContentButton(String commandType, bool enabled)
        {
//            logger.Debug($"* Set enable of MenuContent buttons : {enabled}");

            try
            {
                // Application Menu(큰 'A' 버튼) 버튼 처리

                MenuContent menuContent = ComponentManager.ApplicationMenu.MenuContent;

                if (menuContent != null)
                {
                    this.visitItems(menuContent.Items, item =>
                    {
                        if (this.lookupButton(item, commandType))
                        {
                            item.IsEnabled = enabled;
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                // 리본 구조는 버전에 따라 달라질 수 있으니 예외를 무시하고 로그만 남김
                logger.Debug($" - setEnableMenuContentButton error: {ex.Message}");
            }
        }

        // 리본 버튼 비활성화/활성화
        private void setEnableRibbonButton(String commandType, bool enabled)
        {
//            logger.Debug($"* Set enable of Ribbon button : {enabled}");

            try
            {
                QuickAccessToolBarSource quickAccessToolBarSource = ComponentManager.QuickAccessToolBar;

                // 3. 리본 탭/패널 내부 버튼 처리(저장/다른 이름으로 저장/플롯/퍼블리시 등)
                RibbonControl ribbonControl = ComponentManager.Ribbon;

                if (ribbonControl != null)
                {
                    foreach (RibbonTab tab in ribbonControl.Tabs)
                    {
                        if (tab.Id.Equals("ACAD.ID_TabHome"))
                        {
                            tab.IsActive = true;
                        }

                        // 리본 메뉴 탭 활성화/비활성화 
                        if (this.lookupTab(tab, commandType))
                        {
                            tab.IsEnabled = enabled;
                        }

                        int i = 0;

                        foreach (RibbonPanel panel in tab.Panels)
                        {
                            String panelId = "";

                            if (String.IsNullOrEmpty(panel.Id))
                            {
                                panelId = $"Panel_{i++}";
                            }

                            RibbonItemCollection items = panel.Source.Items;

                            if (items == null)
                            {
                                continue;
                            }

                            this.visitItems(items, item =>
                            {
                                if (this.lookupButton(item, commandType))
                                {
                                    item.IsEnabled = enabled;
                                }
                            });
                        }
                    }

                    // 리본 레이아웃 갱신
                    ribbonControl?.UpdateLayout();
                }
            }
            catch (Exception ex)
            {
                // 리본 구조는 버전에 따라 달라질 수 있으니 예외를 무시하고 로그만 남김
                logger.Debug($" - seEnableRibbonButton error: {ex.Message}");
            }
        }

        // 중첩 리본 아이템 재귀 순회 (Split/Menu/Row/Gallery/Combo 등)
        private void visitItems(IEnumerable<RibbonItem> items, Action<RibbonItem> visitor)
        {
            foreach (RibbonItem item in items)
            {
                if (item == null)
                {
                    continue;
                }

                visitor(item);

                switch (item)
                {
                    case RibbonSplitButton split when split.Items != null:
                        this.visitItems(split.Items, visitor);
                        break;

                    case RibbonRowPanel row when row.Items != null:
                        this.visitItems(row.Items, visitor);
                        break;

                    case RibbonMenuButton menu when menu.Items != null:
                        this.visitItems(menu.Items, visitor);
                        break;

                    case RibbonCombo combo when combo.Items != null:
                        visitItems(combo.MenuItems, visitor);
                        break;
                }
            }
        }

        // 리본 탭 찾기
        private bool lookupTab(RibbonTab item, String commandType)
        {
            if (item == null)
            {
                return false;
            }

            String id = item.Id?.ToUpperInvariant() ?? "";
            String name = item.Name?.ToUpperInvariant() ?? "";

            List<String> idList = DocumentConstants.DICTIONARY_OF_ID.TryGetValue(commandType, out List<String> list) ? list : null;

            foreach (String idVal in idList!)
            {
                if (id.Contains(idVal))
                {
                    // logger.Debug($" - {name.Replace("\r", "")} ({id})");
                    return true;
                }
            }

            return false;
        }

        // 리본 버튼 찾기
        private bool lookupButton(RibbonItem item, String commandType)
        {
            if (item == null)
            {
                return false;
            }

            String id = item.Id?.ToUpperInvariant() ?? "";
            String name = item.Text?.ToUpperInvariant() ?? "";
            String command = (item as RibbonButton)?.CommandParameter?.ToString()?.ToUpperInvariant() ?? "";

            List<String> idList = DocumentConstants.DICTIONARY_OF_ID.TryGetValue(commandType, out List<String> list) ? list : null;
            List<String> commandList = DocumentConstants.DICTIONARY_OF_COMMAND.TryGetValue(commandType, out List<String> cmdList) ? cmdList : null;

            foreach (String idVal in idList!)
            {
                if (id.Contains(idVal))
                {
                    // logger.Debug($" - {name.Replace("\r", "")} ({id}) {command}");
                    return true;
                }
            }

            foreach (String commandVal in commandList!)
            {
                if (command.Contains(commandVal))
                {
                    logger.Debug($" - {name.Replace("\r", "")} ({id}) { command }");
                    return true;
                }
            }

            return false;
        }

        // 리본/퀵 액세스에서 저장 관련 컨트롤을 찾아 비활성화/활성화
        public void printRibbonMenuStructure()
        {
            logger.Debug("-----------------------------------------------------------");
            logger.Debug("Structure of Ribbon menu");
            logger.Debug("-----------------------------------------------------------");

            QuickAccessToolBarSource quickAccessToolBarSource = ComponentManager.QuickAccessToolBar;

            // 1. 퀵 액세스 툴바(좌측 위) 버튼
            if (quickAccessToolBarSource != null)
            {
                logger.Debug($"[QuickAccessToolBar]");

                foreach (RibbonItem item in quickAccessToolBarSource.Items)
                {
                    String id = item.Id?.ToUpperInvariant() ?? "";
                    String text = item.Text?.ToUpperInvariant() ?? "";
                    String command = (item as RibbonButton)?.CommandParameter?.ToString()?.ToUpperInvariant() ?? "";

                    if (!id.Equals("") || !text.Equals("") || !command.Equals(""))
                    {
                        logger.Debug($"    > {text}  ('{id}', '{command}')");
                    }
                }
            }

            // 2. 어플리케이션 메뉴(큰 'A') 리본 버튼
            MenuContent menuContent = ComponentManager.ApplicationMenu.MenuContent;

            if (menuContent != null)
            {
                logger.Debug($"[MenuContent]");

                this.visitItems(menuContent.Items, item =>
                {
                    String id = item.Id?.ToUpperInvariant() ?? "";
                    String text = item.Text?.ToUpperInvariant() ?? "";
                    String command = (item as RibbonButton)?.CommandParameter?.ToString()?.ToUpperInvariant() ?? "";

                    if (!id.Equals("") || !text.Equals("") || !command.Equals(""))
                    {
                        logger.Debug($"    > {text} ('{id}', '{command}')");
                    }
                });
            }

            // 3. 리본 탭/패널 내부 버튼 처리(저장/다른 이름으로 저장/플롯/퍼블리시 등)
            RibbonControl ribbonControl = ComponentManager.Ribbon;

            if (ribbonControl != null)
            {
                logger.Debug($"[Ribbon]");


                foreach (RibbonTab tab in ribbonControl.Tabs)
                {
                    logger.Debug($"    * RibbonTab : '{tab.Name}'.'{tab.Id}'");

                    int i = 0;

                    foreach (RibbonPanel panel in tab.Panels)
                    {
                        String panelId = "";

                        if (String.IsNullOrEmpty(panel.Id))
                        {
                            panelId = $"Panel_{i++}";
                        }

                        logger.Debug($"        * RibbonPanel : '{panelId}'");

                        RibbonItemCollection items = panel.Source.Items;

                        if (items == null)
                        {
                            continue;
                        }

                        this.visitItems(items, item =>
                        {
                            String id = item.Id?.ToUpperInvariant() ?? "";
                            String text = item.Text?.ToUpperInvariant() ?? "";
                            String command = (item as RibbonButton)?.CommandParameter?.ToString()?.ToUpperInvariant() ?? "";

                            if (!id.Equals("") || !text.Equals("") || !command.Equals(""))
                            {
                                logger.Debug($"            > {text.Replace("\r", "")} ('{id}', '{command}')");
                            }
                        });
                    }
                }
            }

            logger.Debug("-----------------------------------------------------------");
        }

        // 클래식 메뉴에서 권한 관련 메뉴 항목 비활성화/활성화 
        private void enableClassicMenuButton(String commandType, bool enabled)
        {
            String menuGroupName = "ACAD";

            logger.Debug($"-----------------------------------------------------------");
            logger.Debug($"'{menuGroupName}' menu enable : {enabled}");
            logger.Debug($"-----------------------------------------------------------");

            try
            {
                // "ACAD" 메뉴 그룹의 메뉴 그룹의 하위 메뉴 아이템의 인덱스나 이름을 통해 접근

                ZcadMenuGroup menuGroup = (CadApplication.AcadApplication as ZcadApplication).MenuGroups.Item(menuGroupName);

                if (menuGroup == null)
                {
                    logger.Debug($"* MenuGroup is null");
                    logger.Debug($"-----------------------------------------------------------");
                    return;
                }

                // 메인 메뉴
                ZcadPopupMenus menus = menuGroup.Menus;

                if (menus == null || menus.Count < 1)
                {
                    logger.Debug($"* Main Menu is null");
                    logger.Debug($"-----------------------------------------------------------");
                    return;
                }

                List<String> menuList = DocumentConstants.DICTIONARY_MENU_INDEX.TryGetValue(commandType, out List<String> list) ? list : null;

                if (menuList == null || menuList.Count < 1)
                {
                    logger.Debug("* MenuList is null");
                    logger.Debug($"-----------------------------------------------------------");
                    return;
                }

                foreach (String menuIndexStr in menuList)
                {
                    if (String.IsNullOrEmpty(menuIndexStr) || !menuIndexStr.Contains(":"))
                    {
                        continue;
                    }

                    String[] menuIndexArr = menuIndexStr.Split(':');

                    if (menuIndexArr.Length != 2)
                    {
                        continue;
                    }

                    int mainMenuIndex = int.Parse(menuIndexArr[0] ?? "999");
                    int subMenuIndex = int.Parse(menuIndexArr[1] ?? "999");

                    // 메인 메뉴 내 특정 하위 메뉴 항목 활성/비활성화
                    menuGroup.Menus.Item(mainMenuIndex).Item(subMenuIndex).Enable = enabled;

                    logger.Debug($"[{mainMenuIndex}. {menuGroup.Menus.Item(mainMenuIndex).Name}] ＞ {subMenuIndex}. {menuGroup.Menus.Item(mainMenuIndex).Item(subMenuIndex).Label} ({menuGroup.Menus.Item(mainMenuIndex).Item(subMenuIndex).Macro})");
                }

                logger.Debug($"-----------------------------------------------------------");
            }
            catch (Exception ex)
            {
                logger.Debug($" - enableMenuButton error: {ex.Message}");
            }
        }

        // ACAD 메뉴 구조 출력 (ACAD 메뉴 그룹의 메인 메뉴 목록에서 하위 메뉴의 인덱스나 이름을 통해 접근)
        public void printClassicMenuStructure()
        {
            // ACAD 메뉴 그룹
            ZcadMenuGroup menuGroup = (CadApplication.AcadApplication as ZcadApplication).MenuGroups.Item("ACAD");

            logger.Debug("-----------------------------------------------------------");
            logger.Debug("Structure of Menu");
            logger.Debug("-----------------------------------------------------------");

            if (menuGroup == null)
            {
                logger.Debug("   > MenuGroup is null");
                logger.Debug("-----------------------------------------------------------");
                return;
            }

            // 메인 메뉴
            ZcadPopupMenus menus = menuGroup.Menus;

            if (menus == null || menus.Count < 1)
            {
                logger.Debug("   > Main Menu is null");
                logger.Debug("-----------------------------------------------------------");
                return;
            }

            for (int i = 0; i < menus.Count; i++)
            {
                // 모든 메인 메뉴 출력

                ZcadPopupMenu mainMenu = menus.Item(i);

                logger.Debug($"[{i}. {mainMenu.Name}]");

                if (mainMenu != null && mainMenu.Count > 0)
                {
                    // 메인 메뉴에 속한 모든 서브 메뉴 출력

                    for (int j = 0; j < mainMenu.Count; j++)
                    {
                        ZcadPopupMenuItem subMenu = mainMenu.Item(j);

                        if (!String.IsNullOrEmpty(subMenu.Label))
                        {
                            logger.Debug($"   {j}. {subMenu.Label}({subMenu.Macro})");
                        }
                    }
                }
                else
                {
                    logger.Debug($"   > subMenu is not exists.");
                }
            }

            logger.Debug("-----------------------------------------------------------");
        }
    
        // 중복 생성 방지용 ID (고유하게 유지)
        private const string TabId = "eDIAN_PLUS_TAB";
        private const string PanelId = "eDIAN_PLUS_PANEL";

        public static void ensureRibbon()
        {
            RibbonControl ribbon = ComponentManager.Ribbon;

            if (ribbon == null)
            {
                // AutoCAD UI가 아직 준비되지 않은 타이밍일 수 있음
                return;
            }

            // 탭이 이미 있으면 종료 (중복 방지)
            if (ribbon.Tabs.Any(t => string.Equals(t.Id, TabId, StringComparison.OrdinalIgnoreCase)) == true)
            {
                return;
            }

            var tab = new RibbonTab
            {
                Id = TabId,
                Title = "edian+",
                IsVisible = true
            };

            var panelSource = new RibbonPanelSource
            {
                Id = PanelId,
                Title = ""
            };

            var panel = new RibbonPanel
            {
                Source = panelSource
            };

            // 버튼 1개 추가 (명령 실행은 Macro로 연결)
            var btn = new RibbonButton
            {
                Text = "패널 표시",
                ShowText = true,
                Size = RibbonItemSize.Large,
                CommandParameter = "EDIAN_MAIN",    // CommandParameter로 명령 연결 (버튼 클릭 시 해당 명령이 실행됨)  "EDIAN_MAIN"은 실제 등록된 CommandMethod 이름으로 교체

                // 아이콘 연결
                LargeImage = PluginFormManager.ToBitmapImage(Properties.Resources.image_setting_on),
                Image = PluginFormManager.ToBitmapImage(Properties.Resources.image_setting_over),
                ShowImage = true
            };

            panelSource.Items.Add(btn);
            tab.Panels.Add(panel);

            if(ribbon.Tabs == null)
            {
                return;
            }

            ribbon.Tabs.Add(tab);
        }
    }
}
