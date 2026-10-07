using System;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;

namespace QuanLyHeThong
{
    /// <summary>Xoá khoá Registry, kể cả các khoá bị Windows khoá quyền (chiếm quyền sở hữu rồi xoá).</summary>
    internal static class RegistryHelper
    {
        /// <summary>Xoá subKeyName (và toàn bộ con) nằm dưới parentPath trong root. Trả về true nếu xoá được.</summary>
        public static bool ForceDeleteSubKey(RegistryKey root, string parentPath, string subKeyName)
        {
            Native.EnablePrivilege("SeTakeOwnershipPrivilege");
            Native.EnablePrivilege("SeRestorePrivilege");
            Native.EnablePrivilege("SeBackupPrivilege");

            // Buoc 1: thu xoa truc tiep
            try
            {
                using (var parent = root.OpenSubKey(parentPath, true))
                {
                    if (parent == null) return true; // khong ton tai coi nhu da sach
                    parent.DeleteSubKeyTree(subKeyName, false);
                    return true;
                }
            }
            catch { /* bi chan quyen -> sang buoc chiem quyen */ }

            // Buoc 2: chiem quyen so huu de quy roi xoa
            try
            {
                string fullChild = string.IsNullOrEmpty(parentPath) ? subKeyName : parentPath + "\\" + subKeyName;
                var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                GrantFullControl(root, fullChild, admins);

                using (var parent = root.OpenSubKey(parentPath, true))
                {
                    if (parent != null) parent.DeleteSubKeyTree(subKeyName, false);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void GrantFullControl(RegistryKey root, string path, SecurityIdentifier admins)
        {
            // Dat chu so huu = Administrators
            try
            {
                using (var key = root.OpenSubKey(path,
                    RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.TakeOwnership))
                {
                    if (key != null)
                    {
                        var sec = key.GetAccessControl(AccessControlSections.Owner);
                        sec.SetOwner(admins);
                        key.SetAccessControl(sec);
                    }
                }
            }
            catch { }

            // Cap FullControl cho Administrators
            try
            {
                using (var key = root.OpenSubKey(path,
                    RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.ChangePermissions))
                {
                    if (key != null)
                    {
                        var sec = key.GetAccessControl(AccessControlSections.Access);
                        sec.AddAccessRule(new RegistryAccessRule(admins, RegistryRights.FullControl,
                            InheritanceFlags.ContainerInherit, PropagationFlags.None, AccessControlType.Allow));
                        key.SetAccessControl(sec);
                    }
                }
            }
            catch { }

            // De quy cho cac khoa con
            try
            {
                using (var key = root.OpenSubKey(path, false))
                {
                    if (key != null)
                    {
                        foreach (var sub in key.GetSubKeyNames())
                            GrantFullControl(root, path + "\\" + sub, admins);
                    }
                }
            }
            catch { }
        }
    }
}
