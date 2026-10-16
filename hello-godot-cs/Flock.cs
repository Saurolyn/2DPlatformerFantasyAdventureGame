using Godot;
using System.Collections.Generic;

public partial class Flock : Node2D
{
	[ExportGroup("Flock Settings")]
	[Export] public int BoidCount { get; set; } = 60;
	[Export] public float MaxSpeed { get; set; } = 220f;
	[Export] public float MaxForce { get; set; } = 500f;

	[ExportGroup("Rule Distances")]
	[Export] public float SeparationRadius { get; set; } = 28f;
	[Export] public float NeighborRadius { get; set; } = 80f;

	[ExportGroup("Rule Weights")]
	[Export] public float SeparationWeight { get; set; } = 3.0f;
	[Export] public float AlignmentWeight { get; set; } = 2.0f;
	[Export] public float CohesionWeight { get; set; } = 0.5f;
	[Export] public float FollowWeight { get; set; } = 2.0f;

	[ExportGroup("Follow & Gather")]
	[Export] public Node2D Target { get; set; }
	[Export] public float GatherRadius { get; set; } = 85f;
	[Export] public float SlowingDistance { get; set; } = 160f;

	[ExportGroup("Toggles")]
	[Export] public bool UseSeparation { get; set; } = true;
	[Export] public bool UseAlignment { get; set; } = true;
	[Export] public bool UseCohesion { get; set; } = true;
	[Export] public bool UseFollow { get; set; } = true;
	[Export] public bool DebugDraw { get; set; } = false;

	private readonly List<Boid> _boids = new();
	private Vector2[] _steering;

	public override void _Ready()
	{
		ZIndex = -1; // flock draws behind the player
		SpawnBoids();
	}

	private void SpawnBoids()
	{
		Vector2 screen = GetViewportRect().Size;
		for (int i = 0; i < BoidCount; i++)
		{
			float angle = GD.Randf() * Mathf.Tau;
			float hue = 0.5f + 0.1f * (GD.Randf() - 0.5f); // teal to sky blue

			var boid = new Boid
			{
				Position = new Vector2(GD.Randf(), GD.Randf()) * screen,
				Velocity = Vector2.FromAngle(angle) * MaxSpeed * 0.5f,
				BodyColor = Color.FromHsv(hue, 0.55f, 1f)
			};
			boid.Rotation = boid.Velocity.Angle();

			AddChild(boid);
			_boids.Add(boid);
		}

		_steering = new Vector2[_boids.Count];
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey keyEvent && keyEvent.Pressed)
		{
			switch (keyEvent.Keycode)
			{
				case Key.Key1: UseSeparation = !UseSeparation; break;
				case Key.Key2: UseAlignment = !UseAlignment; break;
				case Key.Key3: UseCohesion = !UseCohesion; break;
				case Key.Key4: UseFollow = !UseFollow; break;
				case Key.D: DebugDraw = !DebugDraw; break;
			}
		}
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		Vector2 screen = GetViewportRect().Size;

		bool following = UseFollow && IsInstanceValid(Target);
		Vector2 target = following ? ToLocal(Target.GlobalPosition) : Vector2.Zero;

		// 1) Decide: every boid reacts to the same snapshot of the flock.
		for (int i = 0; i < _boids.Count; i++)
		{
			Boid boid = _boids[i];

			Vector2 sep = UseSeparation ? Separation(boid) * SeparationWeight : Vector2.Zero;
			Vector2 ali = UseAlignment ? Alignment(boid) * AlignmentWeight : Vector2.Zero;
			Vector2 coh = UseCohesion ? Cohesion(boid) * CohesionWeight : Vector2.Zero;
			Vector2 fol = Vector2.Zero;

			if (following)
			{
				// Near the leader, fade alignment & cohesion so the flock settles.
				float d = boid.Position.DistanceTo(target);
				float fade = Mathf.Clamp((d - GatherRadius) / SlowingDistance, 0f, 1f);
				ali *= fade;
				coh *= fade;
				fol = Follow(boid, target) * FollowWeight;
			}

			// Save force vectors for debug drawing
			boid.DebugSeparation = sep;
			boid.DebugAlignment = ali;
			boid.DebugCohesion = coh;
			boid.DebugFollow = fol;

			Vector2 steering = sep + ali + coh + fol;
			_steering[i] = steering.LimitLength(MaxForce);
		}

		// 2) Act: move every boid.
		for (int i = 0; i < _boids.Count; i++)
		{
			Boid boid = _boids[i];
			boid.Integrate(_steering[i], MaxSpeed, dt);

			if (!following)
			{
				boid.Position = Wrap(boid.Position, screen);
			}
		}

		QueueRedraw();
	}

	private static Vector2 Wrap(Vector2 p, Vector2 size) =>
		new Vector2(Mathf.PosMod(p.X, size.X), Mathf.PosMod(p.Y, size.Y));

	// Steering helper: steering = desired velocity - velocity, capped at MaxForce.
	private Vector2 SteerTowards(Boid boid, Vector2 direction)
	{
		if (direction.IsZeroApprox()) return Vector2.Zero;
		Vector2 desired = direction.Normalized() * MaxSpeed;
		return (desired - boid.Velocity).LimitLength(MaxForce);
	}

	// Rule 1, Separation: steer away from flockmates that are too close.
	private Vector2 Separation(Boid boid)
	{
		Vector2 push = Vector2.Zero;
		foreach (Boid other in _boids)
		{
			if (other == boid) continue;

			Vector2 away = boid.Position - other.Position;
			float d2 = away.LengthSquared();
			if (d2 > 0f && d2 < SeparationRadius * SeparationRadius)
			{
				push += away / d2; // direction / distance: closer pushes harder
			}
		}
		return SteerTowards(boid, push);
	}

	// Rule 2, Alignment: steer toward the average heading of nearby flockmates.
	private Vector2 Alignment(Boid boid)
	{
		Vector2 headingSum = Vector2.Zero;
		foreach (Boid other in _boids)
		{
			if (other == boid) continue;

			float d2 = boid.Position.DistanceSquaredTo(other.Position);
			if (d2 < NeighborRadius * NeighborRadius)
			{
				headingSum += other.Velocity;
			}
		}
		return SteerTowards(boid, headingSum);
	}

	// Rule 3, Cohesion: steer toward the average position of nearby flockmates.
	private Vector2 Cohesion(Boid boid)
	{
		Vector2 center = Vector2.Zero;
		int count = 0;
		foreach (Boid other in _boids)
		{
			if (other == boid) continue;

			float d2 = boid.Position.DistanceSquaredTo(other.Position);
			if (d2 < NeighborRadius * NeighborRadius)
			{
				center += other.Position;
				count++;
			}
		}
		if (count == 0) return Vector2.Zero;
		center /= count;
		return SteerTowards(boid, center - boid.Position);
	}

	// Leader following: Reynolds' "arrival", aimed at a ring around the leader.
	private Vector2 Follow(Boid boid, Vector2 target)
	{
		Vector2 toTarget = target - boid.Position;
		float distance = toTarget.Length();
		if (distance < 0.01f) return Vector2.Zero;

		float gap = distance - GatherRadius;
		float ramp = gap > 0f ? SlowingDistance : GatherRadius * 0.5f;
		float speed = MaxSpeed * Mathf.Clamp(gap / ramp, -1f, 1f);

		Vector2 desired = toTarget / distance * speed;
		return (desired - boid.Velocity).LimitLength(MaxForce);
	}

	public override void _Draw()
	{
		if (!DebugDraw) return;

		const float scale = 0.04f; // px/s^2 -> readable line length
		var neighborRing = new Color(1f, 1f, 1f, 0.08f);
		var personalRing = new Color(1f, 0.4f, 0.4f, 0.35f);

		foreach (Boid b in _boids)
		{
			Vector2 p = b.Position;
			DrawArc(p, NeighborRadius, 0f, Mathf.Tau, 32, neighborRing);
			DrawArc(p, SeparationRadius, 0f, Mathf.Tau, 16, personalRing);

			DrawLine(p, p + b.DebugSeparation * scale, Colors.Red, 2f);
			DrawLine(p, p + b.DebugAlignment * scale, Colors.LimeGreen, 2f);
			DrawLine(p, p + b.DebugCohesion * scale, Colors.DeepSkyBlue, 2f);
			DrawLine(p, p + b.DebugFollow * scale, Colors.Gold, 2f);
		}

		if (UseFollow && IsInstanceValid(Target))
		{
			Vector2 t = ToLocal(Target.GlobalPosition);
			float outer = GatherRadius + SlowingDistance;
			var faintGold = new Color(1f, 0.84f, 0f, 0.3f);

			DrawArc(t, GatherRadius, 0f, Mathf.Tau, 64, Colors.Gold, 2f);
			DrawArc(t, outer, 0f, Mathf.Tau, 64, faintGold, 1f);
		}
	}
}
