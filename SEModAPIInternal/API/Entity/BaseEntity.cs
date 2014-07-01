﻿using Havok;

using Microsoft.Xml.Serialization.GeneratedAssembly;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

using Sandbox.Common.ObjectBuilders;
using Sandbox.Common.ObjectBuilders.Definitions;
using Sandbox.Common.ObjectBuilders.Voxels;
using Sandbox.Common.ObjectBuilders.VRageData;
using Sandbox.Game.Weapons;

using SEModAPI.API;
using SEModAPI.API.Definitions;

using SEModAPIInternal.API.Common;
using SEModAPIInternal.API.Entity.Sector.SectorObject;
using SEModAPIInternal.API.Utility;
using SEModAPIInternal.Support;

using VRage;
using VRageMath;

namespace SEModAPIInternal.API.Entity
{
	public class BaseEntity : BaseObject
	{
		#region "Constructors and Initializers"

		public BaseEntity(MyObjectBuilder_EntityBase baseEntity)
			: base(baseEntity)
		{
			BaseEntity = baseEntity;
		}

		public BaseEntity(MyObjectBuilder_EntityBase baseEntity, Object backingObject)
			: base(baseEntity, backingObject)
		{
			BaseEntity = baseEntity;
		}

		#endregion

		#region "Properties"

		/// <summary>
		/// Gets the formatted name of an entity
		/// </summary>
		[Browsable(true)]
		[ReadOnly(true)]
		[Description("Formatted Name of an entity")]
		public override string Name
		{
			get
			{
				return GetSubTypeEntity().Name == "" ? GetSubTypeEntity().TypeId.ToString() : GetSubTypeEntity().EntityId.ToString();
			}
		}

		/// <summary>
		/// Entity ID of the object
		/// </summary>
		[Category("Entity")]
		[Browsable(true)]
		[Description("The unique entity ID representing a functional entity in-game")]
		public long EntityId
		{
			get { return GetSubTypeEntity().EntityId; }
			set
			{
				if (GetSubTypeEntity().EntityId == value) return;
				GetSubTypeEntity().EntityId = value;

				Changed = true;
			}
		}

		[Category("Entity")]
		[ReadOnly(true)]
		public MyPersistentEntityFlags2 PersistentFlags
		{
			get { return GetSubTypeEntity().PersistentFlags; }
			set
			{
				if (GetSubTypeEntity().PersistentFlags == value) return;
				GetSubTypeEntity().PersistentFlags = value;
				Changed = true;

				//TODO - Find what the backing field is for this
			}
		}

		[Category("Entity")]
		[Browsable(false)]
		public MyPositionAndOrientation PositionAndOrientation
		{
			get { return GetSubTypeEntity().PositionAndOrientation.GetValueOrDefault(); }
			set
			{
				if (GetSubTypeEntity().PositionAndOrientation.Equals(value)) return;
				GetSubTypeEntity().PositionAndOrientation = value;
				Changed = true;
			}
		}

		[Category("Entity")]
		[TypeConverter(typeof(Vector3TypeConverter))]
		public SerializableVector3 Position
		{
			get { return GetSubTypeEntity().PositionAndOrientation.GetValueOrDefault().Position; }
			set
			{
				if (Position.Equals(value)) return;
				MyPositionAndOrientation? positionOrientation = new MyPositionAndOrientation(value, Forward, Up);
				GetSubTypeEntity().PositionAndOrientation = positionOrientation;
				Changed = true;

				if (BackingObject != null)
					BaseEntityManagerWrapper.GetInstance().UpdateEntityPosition(BackingObject, value);
			}
		}

		[Category("Entity")]
		[TypeConverter(typeof(Vector3TypeConverter))]
		public SerializableVector3 Up
		{
			get { return GetSubTypeEntity().PositionAndOrientation.GetValueOrDefault().Up; }
			set
			{
				if (Up.Equals(value)) return;
				MyPositionAndOrientation? positionOrientation = new MyPositionAndOrientation(Position, Forward, value);
				GetSubTypeEntity().PositionAndOrientation = positionOrientation;
				Changed = true;

				if (BackingObject != null)
					BaseEntityManagerWrapper.GetInstance().UpdateEntityUp(BackingObject, value);
			}
		}

		[Category("Entity")]
		[TypeConverter(typeof(Vector3TypeConverter))]
		public SerializableVector3 Forward
		{
			get { return GetSubTypeEntity().PositionAndOrientation.GetValueOrDefault().Forward; }
			set
			{
				if (Forward.Equals(value)) return;
				MyPositionAndOrientation? positionOrientation = new MyPositionAndOrientation(Position, value, Up);
				GetSubTypeEntity().PositionAndOrientation = positionOrientation;
				Changed = true;

				if (BackingObject != null)
					BaseEntityManagerWrapper.GetInstance().UpdateEntityForward(BackingObject, value);
			}
		}

		#endregion

		#region "Methods"

		/// <summary>
		/// Generates a new in-game entity ID
		/// </summary>
		/// <returns></returns>
		public long GenerateEntityId()
		{
			return BaseEntityManagerWrapper.GenerateEntityId();
		}

		/// <summary>
		/// Method to get the casted instance from parent signature
		/// </summary>
		/// <returns>The casted instance into the class type</returns>
		internal MyObjectBuilder_EntityBase GetSubTypeEntity()
		{
			return (MyObjectBuilder_EntityBase)BaseEntity;
		}

		public void Export<TS>(FileInfo fileInfo) where TS : XmlSerializer1
		{
			BaseEntityManager.SaveContentFile<MyObjectBuilder_EntityBase, TS>(GetSubTypeEntity(), fileInfo);
		}

		#endregion
	}

	public class BaseEntityManager : BaseObjectManager
	{
		#region "Methods"

		new public T NewEntry<T>(MyObjectBuilder_EntityBase source) where T : BaseEntity
		{
			if (!IsMutable) return default(T);

			var newEntry = (T)Activator.CreateInstance(typeof(T), new object[] { source });
			long entityId = newEntry.EntityId;
			if (entityId == 0)
				entityId = newEntry.GenerateEntityId();
			GetInternalData().Add(entityId, newEntry);

			return newEntry;
		}

		#endregion
	}

	public class BaseEntityManagerWrapper
	{
		#region "Attributes"

		protected static BaseEntityManagerWrapper m_instance;

		private Thread m_mainGameThread;

		private static Assembly m_assembly;

		private static Type m_objectManagerType;
		private static Type m_entityBaseType;

		//TODO - Build some sort of Dictionary based structure to hold these temp values
		private static Vector3 m_nextEntityPosition;
		private static Vector3 m_nextEntityVelocity;
		private static Vector3 m_nextEntityAngularVelocity;
		private static Vector3 m_nextEntityUp;
		private static Vector3 m_nextEntityForward;
		private static Object m_nextEntityToUpdate;

		public static string ObjectManagerClass = "5BCAC68007431E61367F5B2CF24E2D6F.CAF1EB435F77C7B77580E2E16F988BED";
		public static string ObjectManagerAction1 = "E017E9CA31926307661D7A6B465C8F96";	//() Object Manager shut down?
		public static string ObjectManagerEntityAction1 = "30E511FF32960AE853909500461285C4";	//(GameEntity) Entity-Close()
		public static string ObjectManagerEntityAction2 = "8C1807427F2EEF4DF981396C4E6A42DD";	//(GameEntity, string, string) Entity-Init()
		public static string ObjectManagerGetResourceLock = "6EF7F983A8061B40A5606D75C890AF07";
		public static string ObjectManagerGetEntityHashSet = "84C54760C0F0DDDA50B0BE27B7116ED8";
		public static string ObjectManagerAddEntity = "E5E18F5CAD1F62BB276DF991F20AE6AF";

		public static string NetworkSerializerClass = "5F381EA9388E0A32A8C817841E192BE8.8EFE49A46AB934472427B7D117FD3C64";
		public static string NetworkSerializerSendEntity = "A6B585C993B43E72219511726BBB0649";

		public static string UtilityClass = "5BCAC68007431E61367F5B2CF24E2D6F.226D9974B43A7269CDD3E322CC8110D5";
		public static string UtilityGenerateEntityId = "3B4924802BEBD1AE13B29920376CE914";

		public static string GameEntityClass = "5BCAC68007431E61367F5B2CF24E2D6F.F6DF01EE4159339113BB9650DEEE1913";
		public static string EntityAction1 = "8CAF5306D8DF29E8140056369D0F1FC1";	//(GameEntity) OnWorldPositionChanged
		public static string EntityAction2 = "1CF14BA21D05D5F9AB6993170E4838FE";	//(GameEntity) UpdateAfterSim - Only if certain flags are set on cube blocks, not sure what yet
		public static string EntityAction3 = "183620F2B4C14EFFC9F34BFBCF35ABCC";	//(GameEntity) ??
		public static string EntityAction4 = "6C1670C128F0A838E0BE20B6EB3FB7C4";	//(GameEntity) ??
		public static string EntityAction5 = "FA752E85660B6101F92B340B994C0F29";	//(GameEntity) ??
		public static string EntityPhysicsObject = "691FA4830C80511C934826203A251981";
		public static string EntityEntityId = "F7E51DBA5F2FD0CCF8BBE66E3573BEAC";
		public static string EntityBool1 = "A0B28D2BCB46F916CFAD5C71B0B68717";	//Should be false for removal
		public static string EntityBool2 = "781725BD1387DD32DE9B25B674FC0A2D";	//Should be false for removal
		public static string EntityGetEntityIdMethod = "53C3FFA07960404AABBEAAF931E5487E";

		public static string PhysicsObjectGetRigidBody = "634E5EC534E45874230868BD089055B1";

		#endregion

		#region "Constructors and Initializers"

		protected BaseEntityManagerWrapper(string basePath)
		{
			m_instance = this;

			m_assembly = Assembly.UnsafeLoadFrom("Sandbox.Game.dll");

			m_objectManagerType = m_assembly.GetType(ObjectManagerClass);
			m_entityBaseType = m_assembly.GetType(GameEntityClass);

			Console.WriteLine("Finished loading GameObjectManagerWrapper");
		}

		public static BaseEntityManagerWrapper GetInstance(string basePath = "")
		{
			if (m_instance == null)
			{
				m_instance = new BaseEntityManagerWrapper(basePath);
			}
			return (BaseEntityManagerWrapper)m_instance;
		}

		#endregion

		#region "Properties"

		public static Type ObjectManagerType
		{
			get { return m_objectManagerType; }
		}

		public static Type BaseEntityType
		{
			get { return m_entityBaseType; }
		}

		public Thread GameThread
		{
			get { return m_mainGameThread; }
			set { m_mainGameThread = value; }
		}

		#endregion

		#region "Methods"

		public HashSet<Object> GetObjectManagerHashSetData()
		{
			try
			{
				MethodInfo getEntityHashSet = m_objectManagerType.GetMethod(ObjectManagerGetEntityHashSet, BindingFlags.Public | BindingFlags.Static);
				var rawValue = getEntityHashSet.Invoke(null, new object[] { });
				HashSet<Object> convertedSet = UtilityFunctions.ConvertHashSet(rawValue);

				return convertedSet;
			}
			catch (Exception ex)
			{
				LogManager.GameLog.WriteLine(ex.ToString());
				return null;
			}
		}

		#region APIEntityLists

		private List<T> GetAPIEntityList<T, TO>(MyObjectBuilderTypeEnum type)
			where T : BaseEntity
			where TO : MyObjectBuilder_EntityBase
		{
			HashSet<Object> rawEntities = GetObjectManagerHashSetData();
			List<T> list = new List<T>();

			foreach (Object entity in rawEntities)
			{
				try
				{
					MyObjectBuilder_EntityBase baseEntity = (MyObjectBuilder_EntityBase)BaseObject.InvokeEntityMethod(entity, "GetObjectBuilder", new object[] { false });

					if (baseEntity.TypeId == type)
					{
						TO objectBuilder = (TO)baseEntity;
						T apiEntity = (T)Activator.CreateInstance(typeof(T), new object[] { objectBuilder, entity });

						list.Add(apiEntity);
					}
				}
				catch (Exception ex)
				{
					LogManager.GameLog.WriteLine(ex.ToString());
				}
			}

			return list;
		}

		public List<CubeGridEntity> GetCubeGrids()
		{
			return GetAPIEntityList<CubeGridEntity, MyObjectBuilder_CubeGrid>(MyObjectBuilderTypeEnum.CubeGrid);
		}

		public List<CharacterEntity> GetCharacters()
		{
			return GetAPIEntityList<CharacterEntity, MyObjectBuilder_Character>(MyObjectBuilderTypeEnum.Character);
		}

		public List<VoxelMap> GetVoxelMaps()
		{
			return GetAPIEntityList<VoxelMap, MyObjectBuilder_VoxelMap>(MyObjectBuilderTypeEnum.VoxelMap);
		}

		public List<FloatingObject> GetFloatingObjects()
		{
			return GetAPIEntityList<FloatingObject, MyObjectBuilder_FloatingObject>(MyObjectBuilderTypeEnum.FloatingObject);
		}

		public List<Meteor> GetMeteors()
		{
			return GetAPIEntityList<Meteor, MyObjectBuilder_Meteor>(MyObjectBuilderTypeEnum.Meteor);
		}

		#endregion

		#region Private

		private static FastResourceLock GetResourceLock()
		{
			try
			{
				FieldInfo field = m_objectManagerType.GetField(ObjectManagerGetResourceLock, BindingFlags.Public | BindingFlags.Static);
				FastResourceLock resourceLock = (FastResourceLock)field.GetValue(null);

				return resourceLock;
			}
			catch (Exception ex)
			{
				LogManager.GameLog.WriteLine(ex.ToString());
				return null;
			}
		}

		private static Object GetEntityPhysicsObject(Object gameEntity)
		{
			try
			{
				MethodInfo getPhysicsObjectMethod = BaseObject.GetEntityMethod(gameEntity, EntityPhysicsObject);
				Object physicsObject = getPhysicsObjectMethod.Invoke(gameEntity, new object[] { });

				return physicsObject;
			}
			catch (Exception ex)
			{
				LogManager.GameLog.WriteLine(ex.ToString());
				return null;
			}
		}

		private static HkRigidBody GetRigidBody(Object gameEntity)
		{
			try
			{
				Object physicsObject = GetEntityPhysicsObject(gameEntity);
				MethodInfo getRigidBodyMethod = BaseObject.GetEntityMethod(physicsObject, PhysicsObjectGetRigidBody);
				HkRigidBody rigidBody = (HkRigidBody)getRigidBodyMethod.Invoke(physicsObject, new object[] { });

				return rigidBody;
			}
			catch (Exception ex)
			{
				LogManager.GameLog.WriteLine(ex.ToString());
				return null;
			}
		}

		#endregion

		#region EntityMethods

		#region "Utility"

		public static long GenerateEntityId()
		{
			try
			{
				Type utilityType = m_assembly.GetType(UtilityClass);
				MethodInfo generateIdMethod = utilityType.GetMethod(UtilityGenerateEntityId, BindingFlags.Public | BindingFlags.Static);
				long entityId = (long)generateIdMethod.Invoke(null, new object[] { });

				return entityId;
			}
			catch (Exception ex)
			{
				LogManager.GameLog.WriteLine("Failed to generate entity id");
				LogManager.GameLog.WriteLine(ex.ToString());
				return 0;
			}
		}

		public static long GetEntityId(Object gameEntity)
		{
			try
			{
				long entityId = (long)BaseObject.InvokeEntityMethod(gameEntity, EntityGetEntityIdMethod);

				return entityId;
			}
			catch (Exception ex)
			{
				LogManager.GameLog.WriteLine("Failed to get entity id");
				LogManager.GameLog.WriteLine(ex.ToString());
				return 0;
			}
		}

		#endregion

		#region "Updates"

		public static bool UpdateEntityId(Object gameEntity, long entityId)
		{
			try
			{
				FieldInfo entityIdField = BaseObject.GetEntityField(gameEntity, EntityEntityId);
				entityIdField.SetValue(gameEntity, entityId);

				return true;
			}
			catch (Exception ex)
			{
				LogManager.GameLog.WriteLine(ex.ToString());
				throw ex;
			}
		}

		public bool UpdateEntityPosition(Object gameEntity, Vector3 position)
		{
			try
			{
				m_nextEntityPosition = position;
				m_nextEntityToUpdate = gameEntity;

				Action action = InternalUpdateEntityPosition;
				SandboxGameAssemblyWrapper.EnqueueMainGameAction(action);

				return true;
			}
			catch (Exception ex)
			{
				LogManager.GameLog.WriteLine(ex.ToString());
				throw ex;
			}
		}

		public bool UpdateEntityVelocity(Object gameEntity, Vector3 velocity)
		{
			try
			{
				m_nextEntityVelocity = velocity;
				m_nextEntityToUpdate = gameEntity;

				Action action = InternalUpdateEntityVelocity;
				SandboxGameAssemblyWrapper.EnqueueMainGameAction(action);

				return true;
			}
			catch (Exception ex)
			{
				LogManager.GameLog.WriteLine(ex.ToString());
				throw ex;
			}
		}

		public bool UpdateEntityAngularVelocity(Object gameEntity, Vector3 velocity)
		{
			try
			{
				m_nextEntityAngularVelocity = velocity;
				m_nextEntityToUpdate = gameEntity;

				Action action = InternalUpdateEntityAngularVelocity;
				SandboxGameAssemblyWrapper.EnqueueMainGameAction(action);

				return true;
			}
			catch (Exception ex)
			{
				LogManager.GameLog.WriteLine(ex.ToString());
				throw ex;
			}
		}

		public bool UpdateEntityUp(Object gameEntity, Vector3 up)
		{
			try
			{
				m_nextEntityUp = up;
				m_nextEntityToUpdate = gameEntity;

				Action action = InternalUpdateEntityUp;
				SandboxGameAssemblyWrapper.EnqueueMainGameAction(action);

				return true;
			}
			catch (Exception ex)
			{
				LogManager.GameLog.WriteLine(ex.ToString());
				throw ex;
			}
		}

		public bool UpdateEntityForward(Object gameEntity, Vector3 forward)
		{
			try
			{
				m_nextEntityForward = forward;
				m_nextEntityToUpdate = gameEntity;

				Action action = InternalUpdateEntityForward;
				SandboxGameAssemblyWrapper.EnqueueMainGameAction(action);

				return true;
			}
			catch (Exception ex)
			{
				LogManager.GameLog.WriteLine(ex.ToString());
				throw ex;
			}
		}

		public bool AddEntity(Object gameEntity)
		{
			try
			{
				m_nextEntityToUpdate = gameEntity;

				Action action = InternalAddEntity;
				SandboxGameAssemblyWrapper.EnqueueMainGameAction(action);

				return true;
			}
			catch (Exception ex)
			{
				LogManager.GameLog.WriteLine(ex.ToString());
				throw ex;
			}
		}

		public bool RemoveEntity(Object gameEntity)
		{
			try
			{
				m_nextEntityToUpdate = gameEntity;
				HkRigidBody havokBody = GetRigidBody(m_nextEntityToUpdate);
				m_nextEntityPosition = Vector3.Multiply(havokBody.Position, 1000);

				Action action = InternalUpdateEntityPosition;
				SandboxGameAssemblyWrapper.EnqueueMainGameAction(action);

				//Slight pause to allow the position change to propagate to the clients
				Thread.Sleep(1000);

				//TODO - Shut off all reactors and solar panels

				//Slight pause to allow reactor shutdown to propagate to the clients
				Thread.Sleep(1000);

				Action action2 = InternalRemoveEntity;
				SandboxGameAssemblyWrapper.EnqueueMainGameAction(action2);

				//TODO - Find a way to turn off all power on the ship as well so it is totally hidden and disabled

				return true;
			}
			catch (Exception ex)
			{
				LogManager.GameLog.WriteLine(ex.ToString());
				throw ex;
			}
		}

		#endregion

		#region "Actions"

		public static void InternalUpdateEntityPosition()
		{
			try
			{
				if (m_nextEntityToUpdate == null)
					return;
				if (GetEntityId(m_nextEntityToUpdate) == 0)
					return;

				if (SandboxGameAssemblyWrapper.IsDebugging)
					Console.WriteLine("Entity '" + GetEntityId(m_nextEntityToUpdate).ToString() + "': Updating position to " + m_nextEntityPosition.ToString());

				HkRigidBody havokBody = GetRigidBody(m_nextEntityToUpdate);
				havokBody.Position = m_nextEntityPosition;
				m_nextEntityPosition = Vector3.Zero;
			}
			catch (Exception ex)
			{
				LogManager.GameLog.WriteLine(ex.ToString());
			}
		}

		public static void InternalUpdateEntityVelocity()
		{
			try
			{
				if (m_nextEntityToUpdate == null)
					return;
				long entityId = GetEntityId(m_nextEntityToUpdate);
				if (entityId == 0)
					return;

				if (SandboxGameAssemblyWrapper.IsDebugging)
					Console.WriteLine("Entity '" + entityId.ToString() + "': Updating velocity to " + m_nextEntityVelocity.ToString());

				HkRigidBody havokBody = GetRigidBody(m_nextEntityToUpdate);
				havokBody.LinearVelocity = m_nextEntityVelocity;
				m_nextEntityVelocity = Vector3.Zero;
			}
			catch (Exception ex)
			{
				LogManager.GameLog.WriteLine(ex.ToString());
			}
		}

		public static void InternalUpdateEntityAngularVelocity()
		{
			try
			{
				if (m_nextEntityToUpdate == null)
					return;
				if (GetEntityId(m_nextEntityToUpdate) == 0)
					return;

				if (SandboxGameAssemblyWrapper.IsDebugging)
					Console.WriteLine("Entity '" + GetEntityId(m_nextEntityToUpdate).ToString() + "': Updating angular velocity to " + m_nextEntityAngularVelocity.ToString());

				HkRigidBody havokBody = GetRigidBody(m_nextEntityToUpdate);
				havokBody.AngularVelocity = m_nextEntityAngularVelocity;
				m_nextEntityAngularVelocity = Vector3.Zero;
			}
			catch (Exception ex)
			{
				LogManager.GameLog.WriteLine(ex.ToString());
			}
		}

		public static void InternalUpdateEntityUp()
		{
			try
			{
				if (m_nextEntityToUpdate == null)
					return;
				if (GetEntityId(m_nextEntityToUpdate) == 0)
					return;

				if (SandboxGameAssemblyWrapper.IsDebugging)
					Console.WriteLine("Entity '" + GetEntityId(m_nextEntityToUpdate).ToString() + "': Updating 'up' to " + m_nextEntityUp.ToString());

				HkRigidBody havokBody = GetRigidBody(m_nextEntityToUpdate);
				//TODO - Figure out how to set the rotation from the 'up' vector and the existing 'forward' vector
				m_nextEntityUp = Vector3.Zero;
			}
			catch (Exception ex)
			{
				LogManager.GameLog.WriteLine(ex.ToString());
			}
		}

		public static void InternalUpdateEntityForward()
		{
			try
			{
				if (m_nextEntityToUpdate == null)
					return;
				if (GetEntityId(m_nextEntityToUpdate) == 0)
					return;

				if (SandboxGameAssemblyWrapper.IsDebugging)
					Console.WriteLine("Entity '" + GetEntityId(m_nextEntityToUpdate).ToString() + "': Updating 'forward' to " + m_nextEntityForward.ToString());

				HkRigidBody havokBody = GetRigidBody(m_nextEntityToUpdate);
				//TODO - Figure out how to set the rotation from the 'forward' vector and the existing 'up' vector
				m_nextEntityForward = Vector3.Zero;
			}
			catch (Exception ex)
			{
				LogManager.GameLog.WriteLine(ex.ToString());
			}
		}

		public static void InternalRemoveEntity()
		{
			try
			{
				if (m_nextEntityToUpdate == null)
					return;
				if (GetEntityId(m_nextEntityToUpdate) == 0)
					return;

				if (SandboxGameAssemblyWrapper.IsDebugging)
					Console.WriteLine("Entity '" + GetEntityId(m_nextEntityToUpdate).ToString() + "': Calling 'Close'");

				BaseObject.InvokeEntityMethod(m_nextEntityToUpdate, "Close");

				//TODO - Figure out what needs to be called to fully broadcast the removal to the clients

				m_nextEntityToUpdate = null;
			}
			catch (Exception ex)
			{
				LogManager.GameLog.WriteLine(ex.ToString());
			}
		}

		public static void InternalAddEntity()
		{
			try
			{
				if (m_nextEntityToUpdate == null)
					return;
				if (GetEntityId(m_nextEntityToUpdate) == 0)
					return;

				if (SandboxGameAssemblyWrapper.IsDebugging)
					Console.WriteLine("Entity '" + GetEntityId(m_nextEntityToUpdate).ToString() + "': Adding to scene ...");

				MethodInfo addEntityMethod = m_objectManagerType.GetMethod(ObjectManagerAddEntity, BindingFlags.Public | BindingFlags.Static);
				addEntityMethod.Invoke(null, new object[] { m_nextEntityToUpdate, true });

				MyObjectBuilder_EntityBase baseEntity = (MyObjectBuilder_EntityBase)BaseObject.InvokeEntityMethod(m_nextEntityToUpdate, "GetObjectBuilder", new object[] { false });
				Type someManager = m_assembly.GetType(NetworkSerializerClass);
				MethodInfo sendEntityMethod = someManager.GetMethod(NetworkSerializerSendEntity, BindingFlags.Public | BindingFlags.Static);
				sendEntityMethod.Invoke(null, new object[] { baseEntity });

				m_nextEntityToUpdate = null;
			}
			catch (Exception ex)
			{
				LogManager.GameLog.WriteLine(ex.ToString());
			}
		}

		#endregion

		#endregion

		#endregion
	}
}
