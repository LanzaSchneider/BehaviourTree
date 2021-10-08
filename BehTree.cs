using System;
using System.Collections;
using System.Collections.Generic;

namespace BehTree 
{
	/// <summary>
	/// 行为状态枚举
	/// </summary>
	public static class StateId
	{
		/// <summary>
		/// 处理中
		/// </summary>
		public const byte Processing = 0x00;

		/// <summary>
		/// 成功
		/// </summary>
		public const byte Success = 0x01;

		/// <summary>
		/// 失败
		/// </summary>
		public const byte Failed = 0x02;
	}

	/// <summary>
	/// 行为树上下文
	/// </summary>
	public class Context 
	{
		/// <summary>
		/// 当前节点的结果
		/// </summary>
		public byte CurrentNodeResult = StateId.Processing;

		/// <summary>
		/// 是否跳出当前的执行序列
		/// 跳出时将会用到 CurrentNodeResult 作为结果
		/// </summary>
		public bool IsCurrentSequenceBreak = false;

		/// <summary>
		/// 层级传递数据栈
		/// </summary>
		public readonly Stack<object> DataStack = new Stack<object>();
	}

	/// <summary>
	/// 节点定义
	/// </summary>
	namespace Node 
	{
		/// <summary>
		/// 基础节点
		/// </summary>
		public abstract class Base
		{
			/// <summary>
			/// 当前状态
			/// </summary>
			public virtual byte CurrentState { get; protected set; }

			/// <summary>
			/// 行为处理方法
			/// 根据各个具体的节点实现
			/// </summary>
			protected abstract IEnumerator Process( Context context );

			/// <summary>
			/// 开始处理
			/// 通常不必重写
			/// </summary>
			public virtual IEnumerator Start( Context context ) 
			{
				var proc = Process( context );
				CurrentState = StateId.Processing;
				while (proc.MoveNext())
					yield return null;
			}
		}

		/// <summary>
		/// 生成器节点
		/// </summary>
		public class Generator<T> : Base where T : Base
		{
			protected readonly Func<Context, T> _Generator;

			public Generator(Func<Context, T> generator ) 
			{
				_Generator = generator;
			}

			protected override IEnumerator Process(Context context)
			{
				T node = _Generator.Invoke( context );
				var proc = node.Start( context );
				while (proc.MoveNext())
					yield return null;
			}
		}

		/// <summary>
		/// 简单行为节点组
		/// </summary>
		namespace Simple
		{
			/// <summary>
			/// 动作节点
			/// </summary>
			public class Action : Base
			{
				private readonly Action<Context> _Action;

				/// <summary>
				/// 传入动作委托
				/// 动作委托中可以利用上下文来改变结果
				/// 不改变结果则默认成功
				/// </summary>
				public Action( Action<Context> action ) 
				{
					_Action = action;
				}

				protected override IEnumerator Process( Context context )
				{
					_Action?.Invoke( context );
					CurrentState = StateId.Success;
					if ( context.CurrentNodeResult != StateId.Processing ) 
					{
						CurrentState = context.CurrentNodeResult;
						context.CurrentNodeResult = StateId.Processing;
					}
					yield break;
				}
			}

			/// <summary>
			/// 协程动作节点
			/// </summary>
			public class ActionCoroutine : Base
			{
				private readonly Func<Context, IEnumerator> _Action;

				/// <summary>
				/// 传入动作委托
				/// 动作委托中可以利用上下文来改变结果
				/// 不改变结果则默认成功
				/// </summary>
				public ActionCoroutine( Func<Context, IEnumerator> action ) 
				{
					_Action = action;
				}

				protected override IEnumerator Process(Context context)
				{
					var proc = _Action?.Invoke(context);
					while (proc != null && proc.MoveNext())
						yield return null;
					CurrentState = StateId.Success;
					if (context.CurrentNodeResult != StateId.Processing)
					{
						CurrentState = context.CurrentNodeResult;
						context.CurrentNodeResult = StateId.Processing;
					}
				}
			}

			/// <summary>
			/// 判断节点
			/// </summary>
			public class Decision : Base
			{
				private Predicate<Context> _Predicate;

				/// <summary>
				/// 传入判断谓词
				/// 结果直接由该谓词执行得到(成功或失败)
				/// </summary>
				public Decision( Predicate<Context> predicate ) 
				{
					_Predicate = predicate;
				}

				protected override IEnumerator Process(Context context)
				{
					CurrentState = _Predicate.Invoke(context) ? StateId.Success : StateId.Failed;
					yield break;
				}
			}
		}

		/// <summary>
		/// 复杂行为节点组
		/// </summary>
		namespace Complex 
		{
			/// <summary>
			/// 序列行为节点
			/// </summary>
			public abstract class Sequential : Base
			{
				/// <summary>
				/// 子节点序列
				/// </summary>
				public readonly Base[] Sequence;

				/// <summary>
				/// 要传递给下级的栈数据
				/// </summary>
				protected readonly object _StackData;

				/// <summary>
				/// 传入栈数据及子行为队列
				/// </summary>
				/// <param name="stackData">栈数据</param>
				/// <param name="sequence">子行为队列</param>
				public Sequential( object stackData, params Base[] sequence ) 
				{
					Sequence = sequence;
					_StackData = stackData;
				}

				/// <summary>
				/// 传入子行为队列
				/// </summary>
				/// <param name="sequence">子行为队列</param>
				public Sequential( params Base[] sequence ) : this( null, sequence )
				{}

				public override IEnumerator Start( Context context )
				{
					var proc = Process( context );
					CurrentState = StateId.Processing;
					context.IsCurrentSequenceBreak = false;
					context.DataStack.Push( _StackData );
					while (proc.MoveNext()) 
					{
						if ( context.IsCurrentSequenceBreak ) 
						{
							CurrentState = context.CurrentNodeResult;
							context.IsCurrentSequenceBreak = false;
							break;
						}
						yield return null;
					}
					context.DataStack.Pop();
				}

				/// <summary>
				/// "逻辑与"行为节点
				/// </summary>
				public class LogicalAnd : Sequential
				{
					/// <summary>
					/// 是否短路逻辑
					/// </summary>
					protected bool _IsShortCircuit;

					/// <summary>
					/// 构造"逻辑与"行为节点
					/// </summary>
					/// <param name="isShortCircuit">是否短路</param>
					/// <param name="stackData">栈数据</param>
					/// <param name="sequence">子行为序列</param>
					public LogicalAnd(bool isShortCircuit, object stackData, params Base[] sequence) :
						base(stackData, sequence)
					{
						_IsShortCircuit = isShortCircuit;
					}

					/// <summary>
					/// 构造"逻辑与"行为节点
					/// </summary>
					/// <param name="isShortCircuit">是否短路</param>
					/// <param name="sequence">子行为序列</param>
					public LogicalAnd(bool isShortCircuit, params Base[] sequence) : this(isShortCircuit, null, sequence)
					{ }

					protected override IEnumerator Process(Context context)
					{
						var result = true;
						foreach (var node in Sequence)
						{
							var proc = node.Start(context);
							while (proc.MoveNext())
								yield return null;
							if (node.CurrentState == StateId.Failed)
							{
								result = false;
								if (_IsShortCircuit)
									break;
							}
						}
						CurrentState = result ? StateId.Success : StateId.Failed;
					}
				}

				/// <summary>
				/// "逻辑或"行为节点
				/// </summary>
				public class LogicalOr : Sequential
				{
					/// <summary>
					/// 是否短路逻辑
					/// </summary>
					protected bool _IsShortCircuit;

					/// <summary>
					/// 构造"逻辑或"行为节点
					/// </summary>
					/// <param name="isShortCircuit">是否短路</param>
					/// <param name="stackData">栈数据</param>
					/// <param name="sequence">子行为序列</param>
					public LogicalOr(bool isShortCircuit, object stackData, params Base[] sequence) :
						base(stackData, sequence)
					{
						_IsShortCircuit = isShortCircuit;
					}

					/// <summary>
					/// 构造"逻辑或"行为节点
					/// </summary>
					/// <param name="isShortCircuit">是否短路</param>
					/// <param name="sequence">子行为序列</param>
					public LogicalOr(bool isShortCircuit, params Base[] sequence) : this(isShortCircuit, null, sequence)
					{ }

					protected override IEnumerator Process(Context context)
					{
						var result = false;
						foreach (var node in Sequence)
						{
							var proc = node.Start(context);
							while (proc.MoveNext())
								yield return null;
							if (node.CurrentState == StateId.Success)
							{
								result = true;
								if (_IsShortCircuit)
									break;
							}
						}
						CurrentState = result ? StateId.Success : StateId.Failed;
					}
				}

				/// <summary>
				/// 必然成功行为节点
				/// </summary>
				public class AlwaysSuccess : Sequential
				{
					protected override IEnumerator Process(Context context)
					{
						var result = false;
						foreach (var node in Sequence)
						{
							var proc = node.Start(context);
							while (proc.MoveNext())
								yield return null;
						}
						CurrentState = StateId.Success;
					}

					/// <summary>
					/// 传入栈数据及子行为队列
					/// </summary>
					/// <param name="stackData">栈数据</param>
					/// <param name="sequence">子行为队列</param>
					public AlwaysSuccess(object stackData, params Base[] sequence) : base(stackData, sequence) { }

					/// <summary>
					/// 传入子行为队列
					/// </summary>
					/// <param name="sequence">子行为队列</param>
					public AlwaysSuccess(params Base[] sequence) : this(null, sequence) { }
				}

				/// <summary>
				/// 无限循环节点
				/// </summary>
				public class Loop : Sequential
				{
					/// <summary>
					/// 传入栈数据及子行为队列
					/// </summary>
					/// <param name="stackData">栈数据</param>
					/// <param name="sequence">子行为队列</param>
					public Loop(object stackData, params Base[] sequence) : base(stackData, sequence) { }

					/// <summary>
					/// 传入子行为队列
					/// </summary>
					/// <param name="sequence">子行为队列</param>
					public Loop(params Base[] sequence) : this(null, sequence) { }

					protected override IEnumerator Process(Context context)
					{
						for (int i = 0, len = Sequence.Length; i < len; i++)
						{
							var node = Sequence[i];
							var proc = node.Start(context);
							while (proc.MoveNext())
								yield return null;
							CurrentState = node.CurrentState;
							if (i == len - 1)
								i = 0;
						}
					}
				}
			}

			/// <summary>
			/// 修饰器行为节点
			/// </summary>
			public abstract class Modifier : Base
			{
				/// <summary>
				/// 修饰对象
				/// </summary>
				protected readonly Base _Target;

				/// <summary>
				/// 传入修饰对象
				/// </summary>
				public Modifier( Base target ) 
				{
					_Target = target;
				}

				/// <summary>
				/// 修饰器的修饰规则
				/// </summary>
				public abstract byte GetModifiedState(byte state);

				protected override IEnumerator Process(Context context)
				{
					var proc = _Target.Start(context);
					while (proc.MoveNext())
						yield return null;
					CurrentState = GetModifiedState( _Target.CurrentState );
				}

				/// <summary>
				/// 必然成功修饰器
				/// </summary>
				public class AlwaysSuccess : Modifier
				{
					public AlwaysSuccess( Base target ) : base(target) { }

					public override byte GetModifiedState(byte state) => StateId.Success;
				}

				/// <summary>
				/// 反转修饰器
				/// </summary>
				public class Inversion : Modifier
				{
					public Inversion(Base target) : base(target) { }

					public override byte GetModifiedState(byte state) => state == StateId.Success ? StateId.Failed : StateId.Success;
				}
			}
		}
	}

	/// <summary>
	/// 行为树
	/// </summary>
	public class Tree 
	{
		protected IEnumerator _Coroutine;

		protected readonly Action _UpdateAction;

		/// <summary>
		/// 构造行为树
		/// </summary>
		/// <param name="root">根节点</param>
		/// <param name="rootData">根数据</param>
		/// <param name="updateAction">额外更新行为</param>
		public Tree( Node.Base root, object rootData = null, Action<Context> updateAction = null) 
		{
			var context = new Context();
			context.DataStack.Push(rootData);
			_Coroutine = root.Start(context);
			if ( updateAction != null ) 
			{
				_UpdateAction = () =>
				{
					updateAction.Invoke(context);
				};
			}
		}

		/// <summary>
		/// 更新行为树
		/// </summary>
		public void Update() 
		{
			if ( _Coroutine != null && _Coroutine.MoveNext() ) 
			{
				_UpdateAction?.Invoke();
				return;
			}
			_Coroutine = null;
		}

		/// <summary>
		/// 行为树是否还能继续执行
		/// </summary>
		public bool IsAlive => _Coroutine != null;

		/// <summary>
		/// 获得内部协程
		/// </summary>
		public IEnumerator Coroutine => _Coroutine;
	}
}
